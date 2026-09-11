using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TangentSpace.Tests.ExperienceIntegration;

/// <summary>Tier A of the cross-server integration: the real web application's experience API
/// over real HTTP with a real verified credential. These assertions are the contract the
/// Rust connector consumes; they run against the genuine stack (real controllers, real
/// authentication handlers, real SQLite persistence), never a fake.</summary>
[Xunit.Collection("Experience integration")]
public sealed class ExperienceApiTests : IAsyncLifetime
{
    private ExperienceWebApp app = null!;

    public async ValueTask InitializeAsync()
    {
        app = await ExperienceWebApp.StartAsync();
    }

    public ValueTask DisposeAsync()
    {
        return app.DisposeAsync();
    }

    private static async Task<JsonDocument> Ok(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("1.0", document.RootElement.GetProperty("experienceVersion").GetString());
        return document;
    }

    [Fact]
    public async Task Arrival_identifies_the_verified_actor_and_the_world()
    {
        using var response = await app.Http.GetAsync("/api/v1/experience");
        using var document = await Ok(response);
        var root = document.RootElement;
        Assert.Equal("arrive", root.GetProperty("operation").GetString());
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal(ExperienceWebApp.AgentDid, root.GetProperty("identity").GetProperty("did").GetString());
        Assert.Equal(ExperienceWebApp.AgentHandle, root.GetProperty("identity").GetProperty("handle").GetString());
        var tangents = root.GetProperty("result").GetProperty("data").GetProperty("tangents");
        // The bootstrap home Tangent plus the seeded Workshop are both visible.
        var workshop = Enumerable.Range(0, tangents.GetArrayLength())
            .Select(index => tangents[index])
            .Single(tangent => tangent.GetProperty("name").GetString() == "Workshop");
        Assert.Equal("member", workshop.GetProperty("membership").GetString());
        Assert.True(root.GetProperty("capabilities").GetProperty("attention").GetBoolean());
    }

    [Fact]
    public async Task The_digest_distinguishes_a_direct_mention_and_a_direct_reply_from_activity()
    {
        using var response = await app.Http.GetAsync("/api/v1/experience/updates");
        using var document = await Ok(response);
        var attention = document.RootElement.GetProperty("attention");
        Assert.Equal(2, attention.GetProperty("waitingCount").GetProperty("value").GetInt32());
        var items = attention.GetProperty("items");
        var mention = Enumerable.Range(0, items.GetArrayLength())
            .Select(index => items[index])
            .Single(item => item.GetProperty("kind").GetString() == "direct_mention");
        Assert.Equal("addressed_to_you", mention.GetProperty("relationship").GetString());
        Assert.Equal(ExperienceWebApp.HumanDid, mention.GetProperty("actorRef").GetString());
        Assert.Equal(ExperienceWebApp.AgentDid, mention.GetProperty("recipientRef").GetString());
        Assert.Equal(app.TopicRef, mention.GetProperty("scopeRef").GetString());
        Assert.Equal($"{app.Origin}::{ExperienceWebApp.TangentKey}::{ExperienceWebApp.TopicKey}::m-leo-m1",
            mention.GetProperty("sourceRef").GetString());
        Assert.Contains("coordinate Project Z", mention.GetProperty("excerpt").GetString());
        var reply = Enumerable.Range(0, items.GetArrayLength())
            .Select(index => items[index])
            .Single(item => item.GetProperty("kind").GetString() == "direct_reply");
        Assert.Equal("replies_to_you", reply.GetProperty("relationship").GetString());
        Assert.NotNull(document.RootElement.GetProperty("continuation").GetProperty("activityCheckpoint").GetString());
    }

    [Fact]
    public async Task Reading_a_topic_returns_accepted_history_and_distinct_cursors()
    {
        using var response = await app.Http.GetAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}?limit=10");
        using var document = await Ok(response);
        var data = document.RootElement.GetProperty("result").GetProperty("data");
        Assert.Equal("Project Z", data.GetProperty("title").GetString());
        var posts = data.GetProperty("posts");
        Assert.Equal(3, posts.GetArrayLength());
        var mention = Enumerable.Range(0, posts.GetArrayLength())
            .Select(index => posts[index])
            .Single(post => post.GetProperty("ref").GetString()!.EndsWith("m-leo-m1"));
        // Source words are preserved verbatim, including the quoted prose.
        Assert.Contains("\"You should review the whole plan.\"", mention.GetProperty("text").GetString());
        var continuation = document.RootElement.GetProperty("continuation");
        Assert.NotNull(continuation.GetProperty("readCursor").GetString());
        // Null fields are omitted on the wire: no page cursor exists for a fresh window.
        Assert.False(continuation.TryGetProperty("activityPageCursor", out _));
    }

    [Fact]
    public async Task A_read_acknowledgement_resynchronizes_the_digest_without_marking_history()
    {
        using var read = await app.Http.GetAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}?limit=10");
        using var readDocument = await Ok(read);
        var cursor = readDocument.RootElement.GetProperty("continuation").GetProperty("readCursor").GetString()!;
        using var acknowledge = await app.Http.PostAsJsonAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/read-position",
            new { readCursor = cursor });
        using var acknowledged = await Ok(acknowledge);
        Assert.Equal("ok", acknowledged.RootElement.GetProperty("status").GetString());

        // The complete resynchronization withdraws the directed requests on the next digest.
        using var updates = await app.Http.GetAsync("/api/v1/experience/updates");
        using var document = await Ok(updates);
        var attention = document.RootElement.GetProperty("attention");
        Assert.Equal(0, attention.GetProperty("waitingCount").GetProperty("value").GetInt32());
        Assert.Equal(0, attention.GetProperty("items").GetArrayLength());

        // History itself is untouched: the posts are still readable.
        using var history = await app.Http.GetAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}?limit=10");
        using var historyDocument = await Ok(history);
        Assert.Equal(3, historyDocument.RootElement.GetProperty("result").GetProperty("data").GetProperty("posts").GetArrayLength());
    }

    [Fact]
    public async Task A_local_write_accepts_immediately_and_becomes_readable_history()
    {
        // ADR 0006: standalone storage. No Spaces, no authority, no source grant — the Post
        // is accepted under current policy and appears as ordinary history.
        var body = new { requestId = "integration-post-1", text = "A standalone reply with no source network anywhere." };
        using var created = await app.Http.PostAsJsonAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/posts", body);
        using var document = await Ok(created);
        var root = document.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        var receipt = root.GetProperty("result").GetProperty("receipt");
        Assert.Equal("integration-post-1", receipt.GetProperty("requestId").GetString());
        Assert.Equal("completed", receipt.GetProperty("state").GetString());
        var postRef = receipt.GetProperty("resultRef").GetString();
        Assert.NotNull(postRef);

        using var read = await app.Http.GetAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}?limit=10");
        using var history = await Ok(read);
        var posts = history.RootElement.GetProperty("result").GetProperty("data").GetProperty("posts");
        Assert.Equal(4, posts.GetArrayLength());
        Assert.Contains(Enumerable.Range(0, posts.GetArrayLength()).Select(index => posts[index]),
            post => post.GetProperty("ref").GetString() == postRef
                && post.GetProperty("text").GetString()!.Contains("standalone reply"));
    }

    [Fact]
    public async Task Redelivering_the_same_package_converges_on_one_post()
    {
        // ADR 0007: idempotency belongs to the atomic upsert at the client-minted identity.
        // A retry (lost response, double click) replays to the same row, never a duplicate.
        var body = new { requestId = "integration-replay-1", text = "The exact same package, delivered twice." };
        using var first = await app.Http.PostAsJsonAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/posts", body);
        using var firstDocument = await Ok(first);
        var firstRef = firstDocument.RootElement.GetProperty("result").GetProperty("receipt").GetProperty("resultRef").GetString();

        using var second = await app.Http.PostAsJsonAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/posts", body);
        using var secondDocument = await Ok(second);
        var secondRef = secondDocument.RootElement.GetProperty("result").GetProperty("receipt").GetProperty("resultRef").GetString();

        Assert.Equal(firstRef, secondRef);
        using var read = await app.Http.GetAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}?limit=10");
        using var history = await Ok(read);
        Assert.Equal(4, history.RootElement.GetProperty("result").GetProperty("data").GetProperty("posts").GetArrayLength());
    }

    [Fact]
    public async Task A_spaces_mode_write_without_a_source_stays_honestly_pending()
    {
        await using var spaces = await ExperienceWebApp.StartAsync("Spaces");
        var body = new { requestId = "integration-spaces-1", text = "A source-backed reply with no source network." };
        using var created = await spaces.Http.PostAsJsonAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/posts", body);
        using var document = await Ok(created);
        var root = document.RootElement;
        Assert.False(root.GetProperty("status").GetString() == "ok");
        var receipt = root.GetProperty("result").GetProperty("receipt");
        Assert.Equal("pending", receipt.GetProperty("state").GetString());
        Assert.NotNull(root.GetProperty("result").GetProperty("problem"));
    }

    [Fact]
    public async Task A_changed_payload_under_the_same_request_id_is_a_conflict()
    {
        // The first write settles (completed, locally accepted); the same key with different
        // content is still a conflict, never a silent second Post.
        var first = new { requestId = "integration-conflict-1", text = "The original payload." };
        using var created = await app.Http.PostAsJsonAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/posts", first);
        await Ok(created);
        var changed = new { requestId = "integration-conflict-1", text = "A different payload entirely." };
        using var response = await app.Http.PostAsJsonAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/posts", changed);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("request_conflict", problem.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Revoking_the_credential_denies_every_private_read_before_serving()
    {
        using var before = await app.Http.GetAsync("/api/v1/experience/updates");
        await Ok(before);
        await app.RevokeAgentCredential();
        using var after = await app.Http.GetAsync("/api/v1/experience/updates");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task An_invalid_bearer_is_rejected_without_anonymous_fallback()
    {
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        anonymous.DefaultRequestHeaders.Authorization = new("Bearer", "ts_invalid_integration_probe");
        using var response = await anonymous.GetAsync("/api/v1/experience");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
