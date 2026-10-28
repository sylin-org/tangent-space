using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Community;
using Koan.Data.Core;
using Tangent.Conversation;
using Tangent.Media;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class PublicWorkspaceWorkflowTests
{
    [Fact]
    public async Task Public_article_metadata_uses_its_readable_opening_cover_and_configured_absolute_origin()
    {
        const string publicOrigin = "https://public.tangent.test";
        await using var app = await ExperienceWebApp.StartAsync(publicOrigin: publicOrigin);
        var ct = TestContext.Current.CancellationToken;
        var tangents = app.Services.GetRequiredService<TangentGovernance>();
        var topics = app.Services.GetRequiredService<TopicGovernance>();
        var conversation = app.Services.GetRequiredService<ConversationService>();
        var media = app.Services.GetRequiredService<PostMediaService>();
        await tangents.Create(app.OwnerParticipantId, "essays", "Essays", null, null, null, null, ct,
            kind: "articles", readAudience: "Public");
        await tangents.CreateTopic(app.OwnerParticipantId, "essays", "covered-article", "Care & craft", "A short description.", ct, TopicReadAudience.Public);
        var cover = await media.Upload(app.OwnerParticipantId, "essays", "covered-article", "cover.png", TestImage.Png(), ct);
        var opening = await conversation.CreatePost(app.OwnerParticipantId, "covered-article",
            new PostCreateRequest("metadata-opening", $"![Article cover](/media/{cover.Id})\n\nThe article body.", Format: PostContent.Markdown), ct);
        var replyImage = await media.Upload(app.OwnerParticipantId, "essays", "covered-article", "reply.png", TestImage.Png(), ct);
        var reply = await conversation.CreatePost(app.OwnerParticipantId, "covered-article",
            new PostCreateRequest("metadata-reply", $"![Reply image](/media/{replyImage.Id})", ReplyTo: opening.Id, Format: PostContent.Markdown), ct);
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        anonymous.DefaultRequestHeaders.Host = "public.tangent.test";
        foreach (var path in new[] { "/t/essays/topics/covered-article", $"/t/essays/topics/covered-article?after={reply.Id}", $"/t/essays/{reply.Id}" })
        {
            using var response = await anonymous.GetAsync(path, ct);
            response.EnsureSuccessStatusCode();
            var document = new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync(ct));
            Assert.Equal(publicOrigin + "/media/" + cover.Id, document.QuerySelector("meta[property='og:image']")!.GetAttribute("content"));
            Assert.Equal(publicOrigin + "/t/essays/topics/covered-article", document.QuerySelector("meta[property='og:url']")!.GetAttribute("content"));
            Assert.Equal("Care & craft", document.QuerySelector("meta[property='og:title']")!.GetAttribute("content"));
            Assert.Equal("summary_large_image", document.QuerySelector("meta[name='twitter:card']")!.GetAttribute("content"));
            Assert.DoesNotContain(app.Origin, document.Head!.InnerHtml);
        }
        await topics.SetReadAudience(app.OwnerParticipantId, "covered-article", TopicReadAudience.Restricted, false, ct);
        using (var hidden = await anonymous.GetAsync("/t/essays/topics/covered-article", ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
            Assert.DoesNotContain("og:image", await hidden.Content.ReadAsStringAsync(ct));
        }
        await topics.SetReadAudience(app.OwnerParticipantId, "covered-article", TopicReadAudience.Public, true, ct);
        await conversation.ChangePost(app.OwnerParticipantId, "covered-article", opening.Id, null, true, ct);
        var removed = new HtmlParser().ParseDocument(await anonymous.GetStringAsync($"/t/essays/{reply.Id}", ct));
        Assert.Null(removed.QuerySelector("meta[property='og:image']"));
        Assert.Null(removed.QuerySelector("meta[name='twitter:card']"));
        Assert.Contains("This post was removed", removed.Body!.TextContent);
    }

    [Fact]
    public async Task Public_permalink_keeps_initial_content_and_resolves_only_public_post_windows()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var root = app.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"),
            "<html><head><script type=\"module\" src=\"/assets/workspace-test.js\"></script><link rel=\"stylesheet\" href=\"/assets/workspace-test.css\"></head><body><div id=\"app\"></div></body></html>", ct);
        var tangents = app.Services.GetRequiredService<TangentGovernance>();
        var topics = app.Services.GetRequiredService<TopicGovernance>();
        await tangents.SetAccess(app.OwnerParticipantId, ExperienceWebApp.TangentKey, new AccessMap(), ct);
        Assert.True((await topics.SetReadAudience(app.OwnerParticipantId, ExperienceWebApp.TopicKey, TopicReadAudience.Public, true, ct)).Accepted);
        var sourced = (await Post.Get("m-leo-r1", ct))!;
        sourced.SourceUri = "at://did:plc:sourceauthor/app.bsky.feed.post/3ltestpost";
        await sourced.Save(ct);
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        using var response = await anonymous.GetAsync("/t/proj/m-leo-r1", ct);
        response.EnsureSuccessStatusCode();
        var document = new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync(ct));
        Assert.Contains("three open questions remain", document.QuerySelector("#public-document")!.TextContent);
        Assert.NotNull(document.QuerySelector("#public-document a[href='https://bsky.app/profile/did%3Aplc%3Asourceauthor/post/3ltestpost']"));
        Assert.NotNull(document.QuerySelector("#public-document form[role=search][action='/search']"));
        Assert.NotNull(document.QuerySelector("script[type=module][src='/assets/workspace-test.js']"));
        Assert.NotNull(document.QuerySelector("#app[hidden]"));
        Assert.Equal("/t/proj/m-leo-r1", document.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Contains("connect-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        var window = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/public/tangents/proj/posts/m-leo-r1", ct);
        Assert.Equal("m-leo-r1", window.GetProperty("anchorId").GetString());
        Assert.Equal("proj-z", window.GetProperty("window").GetProperty("topic").GetProperty("key").GetString());
        Assert.InRange(window.GetProperty("window").GetProperty("posts").GetArrayLength(), 1, 25);
        Assert.Equal(sourced.SourceUri, window.GetProperty("window").GetProperty("posts").EnumerateArray().Single(post => post.GetProperty("id").GetString() == sourced.Id).GetProperty("sourceUri").GetString());
        Assert.All(window.GetProperty("window").GetProperty("posts").EnumerateArray(), post => Assert.StartsWith("/u/", post.GetProperty("author").GetProperty("profileUrl").GetString()));
        await topics.SetReadAudience(app.OwnerParticipantId, ExperienceWebApp.TopicKey, TopicReadAudience.Restricted, false, ct);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/v1/public/tangents/proj/posts/m-leo-r1", ct)).StatusCode);
        anonymous.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/public/tangents/proj/posts/m-leo-r1", ct)).StatusCode);
    }
}
