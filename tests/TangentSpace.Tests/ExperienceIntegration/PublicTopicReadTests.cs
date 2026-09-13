using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using TangentSpace.Participation;
using TangentSpace.Conversation;
using TangentSpace.Rooms;
using Xunit;

namespace TangentSpace.Tests.ExperienceIntegration;

[Xunit.Collection("Experience integration")]
public sealed class PublicTopicReadTests : IAsyncLifetime
{
    private ExperienceWebApp app = null!;

    public async ValueTask InitializeAsync() => app = await ExperienceWebApp.StartAsync();
    public ValueTask DisposeAsync() => app.DisposeAsync();

    [Fact]
    public async Task Topic_is_unlisted_and_restricted_until_its_owner_explicitly_publishes_history()
    {
        using var anonymous = Client();
        var path = $"/api/v1/public/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}";
        using (var restricted = await anonymous.GetAsync(path))
            Assert.Equal(HttpStatusCode.NotFound, restricted.StatusCode);
        using (var restrictedPosts = await anonymous.GetAsync(path + "/posts"))
            Assert.Equal(HttpStatusCode.NotFound, restrictedPosts.StatusCode);
        using (var directory = await anonymous.GetAsync($"/api/v1/public/tangents/{ExperienceWebApp.TangentKey}/topics"))
            Assert.Equal(HttpStatusCode.NotFound, directory.StatusCode);
        using (var restrictedDocument = await anonymous.GetAsync($"/t/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}"))
            Assert.Equal(HttpStatusCode.NotFound, restrictedDocument.StatusCode);
        using (var restrictedPostDocument = await anonymous.GetAsync($"/t/{ExperienceWebApp.TangentKey}/m-agent-q1"))
            Assert.Equal(HttpStatusCode.NotFound, restrictedPostDocument.StatusCode);

        using var owner = await CredentialClient(app.OwnerParticipantId, "public-topic-owner");
        using (var refused = await owner.PatchAsJsonAsync(
                   $"/api/v1/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}/read-audience",
                   new { audience = "Public", publishExistingHistory = false }))
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        using (var published = await owner.PatchAsJsonAsync(
                   $"/api/v1/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}/read-audience",
                   new { audience = "Public", publishExistingHistory = true }))
            Assert.Equal(HttpStatusCode.OK, published.StatusCode);

        using var visible = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        using var document = await JsonDocument.ParseAsync(await visible.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        Assert.Equal(new[] { "key", "path", "readAudience", "tangentKey", "title", "topic" },
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(ExperienceWebApp.TopicKey, root.GetProperty("key").GetString());
        Assert.Equal(ExperienceWebApp.TangentKey, root.GetProperty("tangentKey").GetString());
        Assert.Equal("Project Z", root.GetProperty("title").GetString());
        Assert.Equal("Public", root.GetProperty("readAudience").GetString());
        Assert.Equal($"/t/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}", root.GetProperty("path").GetString());

        using var postResponse = await anonymous.GetAsync(path + "/posts");
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        using var postDocument = await JsonDocument.ParseAsync(await postResponse.Content.ReadAsStreamAsync());
        var posts = postDocument.RootElement.GetProperty("posts");
        Assert.Equal(3, posts.GetArrayLength());
        Assert.All(posts.EnumerateArray(), post =>
        {
            Assert.Equal(new[] { "acceptedAt", "author", "createdAt", "id", "path", "removed", "text" },
                post.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal(new[] { "label", "ref" }, post.GetProperty("author").EnumerateObject()
                .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
            Assert.StartsWith("did:plc:", post.GetProperty("author").GetProperty("ref").GetString());
        });
    }

    [Fact]
    public async Task Invalid_credentials_are_rejected_before_the_public_filter_and_hidden_targets_are_indistinguishable()
    {
        await PublishDirectly();
        using var invalid = Client();
        invalid.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-credential");
        using var rejected = await invalid.GetAsync(
            $"/api/v1/public/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        using var rejectedPosts = await invalid.GetAsync(
            $"/api/v1/public/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}/posts");
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedPosts.StatusCode);
        using var rejectedDocument = await invalid.GetAsync(
            $"/t/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}");
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedDocument.StatusCode);
        using var rejectedPostDocument = await invalid.GetAsync($"/t/{ExperienceWebApp.TangentKey}/m-agent-q1");
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedPostDocument.StatusCode);

        using var anonymous = Client();
        using var wrongParent = await anonymous.GetAsync(
            $"/api/v1/public/tangents/wrong-parent/topics/{ExperienceWebApp.TopicKey}");
        using var missing = await anonymous.GetAsync(
            $"/api/v1/public/tangents/{ExperienceWebApp.TangentKey}/topics/missing-topic");
        Assert.Equal(HttpStatusCode.NotFound, wrongParent.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await wrongParent.Content.ReadAsStringAsync());
        using var wrongParentDocument = await anonymous.GetAsync("/t/wrong-parent/m-agent-q1");
        using var missingDocument = await anonymous.GetAsync($"/t/{ExperienceWebApp.TangentKey}/missing-post");
        Assert.Equal(HttpStatusCode.NotFound, wrongParentDocument.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingDocument.StatusCode);
        Assert.Equal(await missingDocument.Content.ReadAsStringAsync(), await wrongParentDocument.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Anonymous_topic_and_deep_post_links_are_readable_escaped_documents_with_stable_navigation()
    {
        await PublishDirectly();
        using (EntityContext.NoCache())
        {
            var message = await Message.Get("m-agent-q1") ?? throw new InvalidOperationException("Seeded post missing.");
            message.Content = message.Content with { Text = "A literal <script>alert('no')</script> stays text." };
            await message.Save();
            var renamed = await Room.Get(ExperienceWebApp.TopicKey) ?? throw new InvalidOperationException("Seeded Topic missing.");
            renamed.Title = "Project Z revisited";
            await renamed.Save();
        }

        using var anonymous = Client();
        var topicPath = $"/t/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}";
        using var topic = await anonymous.GetAsync(topicPath);
        Assert.Equal(HttpStatusCode.OK, topic.StatusCode);
        Assert.Equal("text/html", topic.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-store", topic.Headers.CacheControl?.ToString());
        Assert.Contains("default-src 'none'", topic.Headers.GetValues("Content-Security-Policy").Single());
        var topicHtml = await topic.Content.ReadAsStringAsync();
        Assert.Contains("<link rel=\"canonical\" href=\"" + topicPath + "\">", topicHtml);
        Assert.Contains("Project Z revisited", topicHtml);
        Assert.Contains("three open questions remain", topicHtml);
        Assert.Contains("&lt;script&gt;alert(&#39;no&#39;)&lt;/script&gt;", topicHtml);
        Assert.DoesNotContain("<script>", topicHtml);
        Assert.Contains($"href=\"/t/{ExperienceWebApp.TangentKey}/m-agent-q1\"", topicHtml);

        using var deep = await anonymous.GetAsync($"/t/{ExperienceWebApp.TangentKey}/m-leo-r1");
        Assert.Equal(HttpStatusCode.OK, deep.StatusCode);
        var deepHtml = await deep.Content.ReadAsStringAsync();
        Assert.Contains("id=\"post-m-leo-r1\" aria-current=\"true\"", deepHtml);
        Assert.Contains("A literal &lt;script&gt;", deepHtml);
        Assert.Contains("three open questions remain", deepHtml);
        Assert.Contains("can you help us coordinate", deepHtml);
        Assert.Contains($"<link rel=\"canonical\" href=\"/t/{ExperienceWebApp.TangentKey}/m-leo-r1\">", deepHtml);

        using var around = await anonymous.GetAsync(
            $"/api/v1/public/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}/posts?around=m-leo-r1");
        Assert.Equal(HttpStatusCode.OK, around.StatusCode);
        using var aroundJson = JsonDocument.Parse(await around.Content.ReadAsByteArrayAsync());
        Assert.Contains(aroundJson.RootElement.GetProperty("posts").EnumerateArray(),
            post => post.GetProperty("id").GetString() == "m-leo-r1");

    }

    [Fact]
    public async Task Public_history_is_count_and_byte_bounded_with_stable_sequence_edges()
    {
        await PublishDirectly();
        using (var context = EntityContext.NoCache())
        {
            for (var sequence = 4; sequence <= 33; sequence++)
            {
                await new Message
                {
                    Id = "public-load-" + sequence,
                    RoomKey = ExperienceWebApp.TopicKey,
                    AuthorParticipantId = app.HumanParticipantId,
                    SourceUri = "at://private-source/" + sequence,
                    SourceCid = "private-cid-" + sequence,
                    Sequence = sequence,
                    AcceptedAt = DateTimeOffset.UtcNow.AddSeconds(sequence),
                    Content = new MessageContent(new string('x', 4096), DateTimeOffset.UtcNow.AddSeconds(sequence), null),
                    Facets = []
                }.Save();
            }
        }
        using var anonymous = Client();
        var path = $"/api/v1/public/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}/posts";
        using var response = await anonymous.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length < 68 * 1024, $"Public window was {bytes.Length} bytes.");
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var posts = root.GetProperty("posts");
        Assert.InRange(posts.GetArrayLength(), 1, 25);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("olderBefore").ValueKind);
        Assert.False(root.TryGetProperty("newerAfter", out _));
        var first = root.GetProperty("olderBefore").GetInt64();

        using var older = await anonymous.GetAsync(path + "?before=" + first + "&limit=25");
        Assert.Equal(HttpStatusCode.OK, older.StatusCode);
        Assert.True((await older.Content.ReadAsByteArrayAsync()).Length < 68 * 1024);

        using var html = await anonymous.GetAsync($"/t/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}");
        Assert.Equal(HttpStatusCode.OK, html.StatusCode);
        var htmlBytes = await html.Content.ReadAsByteArrayAsync();
        Assert.True(htmlBytes.Length < 72 * 1024, $"Public document was {htmlBytes.Length} bytes.");
        Assert.Contains("rel=\"prev\"", System.Text.Encoding.UTF8.GetString(htmlBytes));
    }

    [Fact]
    public async Task A_non_owner_cannot_publish_even_with_the_coarse_management_transport_grant()
    {
        using var agent = await CredentialClient(app.AgentParticipantId, "public-topic-non-owner");
        using var response = await agent.PatchAsJsonAsync(
            $"/api/v1/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}/read-audience",
            new { audience = "Public", publishExistingHistory = true });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var context = EntityContext.NoCache();
        Assert.Equal(RoomReadAudience.Restricted, (await Room.Get(ExperienceWebApp.TopicKey))!.ReadAudience);
    }

    private HttpClient Client() => new() { BaseAddress = new Uri(app.Origin) };

    private async Task<HttpClient> CredentialClient(string participantId, string label)
    {
        var now = app.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        var (credential, token) = ParticipantCredential.Issue(participantId, label, 1,
            [ParticipationGrants.Read, ParticipationGrants.Manage], now, managementPermitted: true);
        using (EntityContext.NoCache()) await credential.Save();
        var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task PublishDirectly()
    {
        using var context = EntityContext.NoCache();
        var topic = await Room.Get(ExperienceWebApp.TopicKey) ?? throw new InvalidOperationException("Seeded Topic missing.");
        topic.ReadAudience = RoomReadAudience.Public;
        await topic.Save();
    }
}
