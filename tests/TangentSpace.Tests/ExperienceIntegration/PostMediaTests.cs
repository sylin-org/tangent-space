using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Media;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class PostMediaTests
{
    [Fact]
    public async Task Uploaded_images_follow_draft_ownership_post_binding_and_live_audience()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        using var owner = new HttpClient { BaseAddress = new Uri(app.Origin) };
        owner.DefaultRequestHeaders.Authorization = new("Bearer", app.OwnerToken);
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        var png = TestImage.Png();
        var upload = new { tangentKey = ExperienceWebApp.TangentKey, topicKey = ExperienceWebApp.TopicKey,
            fileName = "article.png", content = png };
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/media", upload, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/media", new {
            tangentKey = ExperienceWebApp.TangentKey, topicKey = ExperienceWebApp.TopicKey,
            fileName = "unsafe.svg", content = "<svg onload='alert(1)'/>"u8.ToArray() }, ct)).StatusCode);
        var response = await owner.PostAsJsonAsync("/api/media", upload, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var url = receipt.RootElement.GetProperty("url").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(url, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Http.GetAsync(url, ct)).StatusCode);

        var posts = app.Services.GetRequiredService<ConversationService>();
        var text = $"## A complete article\n\n![The article image]({url})\n\nBody.";
        await Assert.ThrowsAsync<ArgumentException>(() => posts.CreatePost(app.AgentParticipantId,
            ExperienceWebApp.TopicKey, new PostCreateRequest("foreign-media", text, Format: "markdown"), ct));
        var post = await posts.CreatePost(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            new PostCreateRequest("own-media", text, Format: "markdown"), ct);
        var retry = await posts.CreatePost(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            new PostCreateRequest("own-media", text, Format: "markdown"), ct);
        Assert.Equal(post.Id, retry.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => posts.CreatePost(app.OwnerParticipantId,
            ExperienceWebApp.TopicKey, new PostCreateRequest("another-post", text, Format: "markdown"), ct));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url, ct)).StatusCode);

        var tangents = app.Services.GetRequiredService<TangentGovernance>();
        var topics = app.Services.GetRequiredService<TopicGovernance>();
        await tangents.SetAccess(app.OwnerParticipantId, ExperienceWebApp.TangentKey, new AccessMap(), ct);
        Assert.True((await topics.SetReadAudience(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            TopicReadAudience.Public, true, ct)).Accepted);
        using (var image = await anonymous.GetAsync(url, ct))
        {
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            Assert.Equal("image/webp", image.Content.Headers.ContentType?.MediaType);
            Assert.True(image.Headers.CacheControl?.NoStore);
            using var decoded = SkiaSharp.SKBitmap.Decode(await image.Content.ReadAsByteArrayAsync(ct));
            Assert.Equal(8, decoded.Width);
            Assert.Equal(8, decoded.Height);
        }
        await tangents.SetAccess(app.OwnerParticipantId, ExperienceWebApp.TangentKey,
            new AccessMap { See = [] }, ct);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(url, ct)).StatusCode);
        await posts.ChangePost(app.OwnerParticipantId, ExperienceWebApp.TopicKey, post.Id, null, true, ct);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(url, ct)).StatusCode);
    }
}
