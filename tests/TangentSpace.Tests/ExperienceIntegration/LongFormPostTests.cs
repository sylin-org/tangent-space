using System.Net;
using System.Text.Json;
using AngleSharp.Html.Parser;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Api;
using Tangent.Community;
using Tangent.Conversation;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class LongFormPostTests
{
    [Fact]
    public void Markdown_rendering_blocks_raw_html_and_unsafe_urls()
    {
        var html = PostMarkup.Render("# Heading\n\n<script>alert(1)</script>\n\n" +
            "[safe](https://example.org/story) ![photo](/media/123) " +
            "[bad](javascript:alert%281%29) ![bad](data:image/svg+xml;base64,PHN2Zz4=) " +
            "[protocol](//evil.example/x) **bold**");
        Assert.Contains("<h1>Heading</h1>", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("https://example.org/story", html);
        Assert.Contains("src=\"/media/123\"", html);
        Assert.Contains("referrerpolicy=\"no-referrer\"", html);
        Assert.Contains("loading=\"lazy\"", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:image", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("//evil.example", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Markdown_image_captions_become_figures_only_for_emphasis_only_lines()
    {
        var html = PostMarkup.Render("![First](/media/first) *Same paragraph*\n\n" +
            "![Second](/media/second)\n\n*Separate paragraph with [credit](https://example.org)*\n\n" +
            "![Third](/media/third)\n\n*This is prose* with more words.");
        var document = new HtmlParser().ParseDocument("<body>" + html + "</body>");
        var figures = document.QuerySelectorAll("figure");
        Assert.Equal(2, figures.Length);
        Assert.Equal("Same paragraph", figures[0]!.QuerySelector("figcaption")?.TextContent);
        Assert.Equal("Separate paragraph with credit", figures[1]!.QuerySelector("figcaption")?.TextContent);
        Assert.Equal("https://example.org", figures[1]!.QuerySelector("figcaption a")?.GetAttribute("href"));
        Assert.NotNull(document.QuerySelector("p > img[src='/media/third']"));
        Assert.Contains("<em>This is prose</em> with more words.", html);
    }

    [Fact]
    public async Task Markdown_format_survives_replay_edits_and_snapshots()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var conversation = app.Services.GetRequiredService<ConversationService>();
        Assert.True((await app.Services.GetRequiredService<TopicGovernance>().SetSettings(app.OwnerParticipantId,
            ExperienceWebApp.TopicKey, true, null, null, null, ct)).Accepted);
        var input = new PostCreateRequest("longform-replay", "# Opening", Format: PostContent.Markdown);
        var post = await conversation.CreatePost(app.AgentParticipantId, ExperienceWebApp.TopicKey, input, ct);
        var replay = await conversation.CreatePost(app.AgentParticipantId, ExperienceWebApp.TopicKey, input, ct);
        Assert.Equal(post.Id, replay.Id);
        Assert.Equal(PostContent.Markdown, replay.Content.Format);
        await Assert.ThrowsAsync<OperationConflictException>(() => conversation.CreatePost(
            app.AgentParticipantId, ExperienceWebApp.TopicKey,
            input with { Format = PostContent.Plain }, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => conversation.CreatePost(
            app.AgentParticipantId, ExperienceWebApp.TopicKey,
            input with { OperationId = "longform-facets", Facets = [new PostFacet { Kind = PostFacet.Tag, Start = 0, End = 1, Value = "x" }] }, ct));
        await conversation.ChangePost(app.AgentParticipantId, ExperienceWebApp.TopicKey, post.Id,
            "## Revised", false, ct);
        using var fresh = EntityContext.NoCache();
        var edited = (await Post.Get(post.Id, ct))!;
        Assert.Equal(PostContent.Markdown, edited.Content.Format);
        Assert.Empty(edited.Facets!);
        var snapshot = (await Post.Get(edited.ChangeId!, Post.ChangelogPartition, ct))!;
        Assert.Equal(PostContent.Markdown, snapshot.Content.Format);
        Assert.Equal("# Opening", snapshot.Content.Text);
        using (var snapshotResponse = await app.Http.GetAsync(
            $"/api/history/messages/{snapshot.Id}?set=changelog&postId={post.Id}", ct))
        {
            snapshotResponse.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await snapshotResponse.Content.ReadAsStringAsync(ct));
            Assert.Equal("markdown", json.RootElement.GetProperty("content").GetProperty("format").GetString());
            Assert.Contains("<h1>Opening</h1>", json.RootElement.GetProperty("content").GetProperty("html").GetString());
        }
        await app.Services.GetRequiredService<TangentGovernance>().SetAccess(app.OwnerParticipantId,
            ExperienceWebApp.TangentKey, new AccessMap(), ct);
        Assert.True((await app.Services.GetRequiredService<TopicGovernance>().SetReadAudience(app.OwnerParticipantId,
            ExperienceWebApp.TopicKey, TopicReadAudience.Public, true, ct)).Accepted);
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        using var page = await anonymous.GetAsync($"/t/{ExperienceWebApp.TangentKey}/{post.Id}", ct);
        page.EnsureSuccessStatusCode();
        Assert.Contains("<h2>Revised</h2>", await page.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task Largest_post_fits_signed_in_and_public_anchored_windows()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var conversation = app.Services.GetRequiredService<ConversationService>();
        var source = new string('<', PostContent.MaximumTextBytes);
        var post = await conversation.CreatePost(app.AgentParticipantId, ExperienceWebApp.TopicKey,
            new PostCreateRequest("longform-largest", source), ct);
        using (var signed = await app.Http.GetAsync($"/api/v1/tangents/{ExperienceWebApp.TangentKey}/posts/{post.Id}", ct))
        {
            signed.EnsureSuccessStatusCode();
            var body = await signed.Content.ReadAsByteArrayAsync(ct);
            Assert.True(body.Length <= WindowPlanner.ResultBudget, $"Signed-in window: {body.Length} bytes");
            using var json = JsonDocument.Parse(body);
            Assert.Contains(json.RootElement.GetProperty("window").GetProperty("messages").EnumerateArray(),
                row => row.GetProperty("id").GetString() == post.Id);
        }
        using (var history = await app.Http.GetAsync($"/api/topics/{ExperienceWebApp.TopicKey}/posts?from=start", ct))
        {
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            var body = await history.Content.ReadAsByteArrayAsync(ct);
            Assert.True(body.Length <= WindowPlanner.ResultBudget, $"History page: {body.Length} bytes");
            using var json = JsonDocument.Parse(body);
            Assert.Contains(json.RootElement.GetProperty("messages").EnumerateArray(),
                row => row.GetProperty("id").GetString() == post.Id);
        }
        await app.Services.GetRequiredService<TangentGovernance>().SetAccess(app.OwnerParticipantId,
            ExperienceWebApp.TangentKey, new AccessMap(), ct);
        Assert.True((await app.Services.GetRequiredService<TopicGovernance>().SetReadAudience(app.OwnerParticipantId,
            ExperienceWebApp.TopicKey, TopicReadAudience.Public, true, ct)).Accepted);
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        using var publicResponse = await anonymous.GetAsync(
            $"/api/v1/public/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}/posts?around={post.Id}", ct);
        publicResponse.EnsureSuccessStatusCode();
        Assert.True((await publicResponse.Content.ReadAsByteArrayAsync(ct)).Length <= WindowPlanner.ResultBudget);
        var publicWindow = (await app.Services.GetRequiredService<PublicConversationReader>()
            .ReadPost(ExperienceWebApp.TangentKey, post.Id, 25, ct))!;
        Assert.Contains(publicWindow.Window.Posts, item => item.Id == post.Id);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(publicWindow.Window).Length <= 1024 * 1024);
    }
}
