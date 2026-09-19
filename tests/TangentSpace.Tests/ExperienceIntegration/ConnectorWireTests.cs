using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Data.Core;
using Tangent.Identity;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

/// <summary>The connector matched pair, server side: the ladder widens steward envelopes and
/// answers through one management endpoint; the member-ring writes and the participant read
/// speak the shapes the sibling connector's grammar waits for. These tests run the real HTTP
/// contract the connector binary exercises in <see cref="ConnectorIntegrationTests"/>.</summary>
[Xunit.Collection("Experience integration")]
public sealed class ConnectorWireTests
{
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.Clone();
    }

    private static void UseToken(HttpClient http, string token)
        => http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Steward_envelopes_offer_exactly_the_rungs_the_roles_carry()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        using var member = await app.Http.GetAsync("/api/v1/experience/topics/proj-z", TestContext.Current.CancellationToken);
        member.EnsureSuccessStatusCode();
        var memberActions = (await ReadAsync(member)).GetProperty("place").GetProperty("allowedActions");
        Assert.DoesNotContain(memberActions.EnumerateArray(), action => action.GetString() == "warn_user");
        Assert.DoesNotContain(memberActions.EnumerateArray(), action => action.GetString() == "list_moderation_cases");

        UseToken(app.Http, app.OwnerToken);
        using var owner = await app.Http.GetAsync("/api/v1/experience/topics/proj-z", TestContext.Current.CancellationToken);
        owner.EnsureSuccessStatusCode();
        var document = await ReadAsync(owner);
        Assert.True(document.GetProperty("capabilities").GetProperty("stewardship").GetBoolean());
        var rungs = document.GetProperty("place").GetProperty("allowedActions").EnumerateArray()
            .Select(action => action.GetString()).Where(action => action is not null).ToHashSet();
        Assert.Superset(new HashSet<string> { "warn_user", "timeout_user", "suspend_user", "ban_user", "assign_roles" }, rungs!);
    }

    [Fact]
    public async Task The_ladder_runs_through_one_management_endpoint_and_refuses_the_rest()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.OwnerToken);

        // Warn: communicative, journaled as moderation, never a sanction.
        using (var warn = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.HumanParticipantId}/management",
            new { action = "warn", reason = "Please keep the tone collegial." }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, warn.StatusCode);
            var data = (await ReadAsync(warn)).GetProperty("result").GetProperty("data");
            Assert.Equal("warn", data.GetProperty("action").GetString());
            Assert.Equal(app.HumanParticipantId, data.GetProperty("userRef").GetString());
        }

        // Timeout and ban cite the case that grounds them; the case names the Tangent.
        var caseRef = await ReportHumanPostAsOwner(app);
        using (var timeout = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.HumanParticipantId}/management",
            new { action = "timeout", reason = "Cool off.", durationSeconds = 3600, caseRef }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, timeout.StatusCode);
            var data = (await ReadAsync(timeout)).GetProperty("result").GetProperty("data");
            Assert.True(data.GetProperty("until").GetDateTimeOffset() > DateTimeOffset.UtcNow);
            Assert.EndsWith("::proj", data.GetProperty("tangentRef").GetString());
        }
        using (var ungrounded = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.HumanParticipantId}/management",
            new { action = "timeout", reason = "No case cited.", durationSeconds = 3600 }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, ungrounded.StatusCode);
        }
        using (var timedBan = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.HumanParticipantId}/management",
            new { action = "ban", reason = "Bans are indefinite here.", durationSeconds = 60, caseRef }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, timedBan.StatusCode);
        }

        // The rung requires the permission: a member cannot climb, however politely.
        UseToken(app.Http, app.AgentToken);
        using (var member = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.HumanParticipantId}/management",
            new { action = "warn", reason = "A member has no rungs." }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, member.StatusCode);
        }
    }

    [Fact]
    public async Task Suspend_takes_the_participant_server_wide_and_the_arrival_refuses_thereafter()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var (humanToken, _) = await IssueHumanCredential(app);
        UseToken(app.Http, app.OwnerToken);
        using var suspend = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.HumanParticipantId}/management",
            new { action = "suspend", reason = "Repeated conduct warnings." }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);

        UseToken(app.Http, humanToken);
        using var arrival = await app.Http.GetAsync("/api/v1/experience", TestContext.Current.CancellationToken);
        Assert.True(arrival.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"a suspended participant's arrival must be refused, got {(int)arrival.StatusCode}");
    }

    [Fact]
    public async Task Assign_roles_adds_and_removes_but_never_beyond_what_the_actor_holds()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.OwnerToken);
        using (var add = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.AgentParticipantId}/management",
            new { action = "add_role", reason = "Trusted with more.", role = "role:member" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        }
        using (var remove = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.AgentParticipantId}/management",
            new { action = "remove_role", reason = "Right-sizing.", role = "member" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        }
        // The derived roles are not grantable at all: everyone signed in holds them already.
        using (var derived = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.AgentParticipantId}/management",
            new { action = "add_role", reason = "Nothing to grant.", role = "role:participant" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, derived.StatusCode);
        }
        // The Owner role is not a ladder rung for anyone.
        using (var ownerRole = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.AgentParticipantId}/management",
            new { action = "add_role", reason = "No.", role = "owner" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, ownerRole.StatusCode);
        }
    }

    [Fact]
    public async Task A_thread_starts_with_one_job_replays_onto_itself_and_carries_its_opening_post()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        var body = new { requestId = "thread-replay-1", title = "Connector Threads", text = "Starting a thread from the wire." };
        using var first = await app.Http.PostAsJsonAsync("/api/v1/experience/tangents/proj/topics", body, TestContext.Current.CancellationToken);
        first.EnsureSuccessStatusCode();
        var data = (await ReadAsync(first)).GetProperty("result").GetProperty("data");
        var topicRef = data.GetProperty("topicRef").GetString()!;
        var postRef = data.GetProperty("postRef").GetString()!;

        using var replay = await app.Http.PostAsJsonAsync("/api/v1/experience/tangents/proj/topics", body, TestContext.Current.CancellationToken);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(topicRef, (await ReadAsync(replay)).GetProperty("result").GetProperty("data").GetProperty("topicRef").GetString());

        var window = await ReadTopic(app.Http, topicRef);
        var windowPosts = window.GetProperty("result").GetProperty("data").GetProperty("posts");
        Assert.Equal(1, windowPosts.GetArrayLength());
        Assert.Equal(postRef, windowPosts[0].GetProperty("ref").GetString());
    }

    [Fact]
    public async Task An_author_edits_and_deletes_own_posts_through_the_wire()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.OwnerToken);
        using (var settings = await app.Http.PatchAsync("/api/rooms/proj-z/settings",
            JsonContent.Create(new { allowPostEditing = true, isLocked = false }), TestContext.Current.CancellationToken))
        {
            settings.EnsureSuccessStatusCode();
        }
        UseToken(app.Http, app.AgentToken);
        var started = await app.Http.PostAsJsonAsync("/api/v1/experience/tangents/proj/topics",
            new { requestId = "thread-edit-1", title = "Edit Path", text = "Original words." }, TestContext.Current.CancellationToken);
        started.EnsureSuccessStatusCode();
        var startedData = (await ReadAsync(started)).GetProperty("result").GetProperty("data");
        var topicRef = startedData.GetProperty("topicRef").GetString()!;
        var topicKey = topicRef.Split("::")[^1];
        var postRef = startedData.GetProperty("postRef").GetString()!;
        var postId = postRef.Split("::")[^1];

        // The author may edit only where the Topic allows it; the new thread starts closed.
        UseToken(app.Http, app.OwnerToken);
        using (var settings = await app.Http.PatchAsync($"/api/rooms/{topicKey}/settings",
            JsonContent.Create(new { allowPostEditing = true, isLocked = false }), TestContext.Current.CancellationToken))
        {
            settings.EnsureSuccessStatusCode();
        }
        UseToken(app.Http, app.AgentToken);

        using (var edit = await app.Http.PatchAsync($"/api/v1/experience/posts/{postId}",
            JsonContent.Create(new { text = "Revised words." }), TestContext.Current.CancellationToken))
        {
            edit.EnsureSuccessStatusCode();
        }
        var window = await ReadTopic(app.Http, topicRef);
        var edited = window.GetProperty("result").GetProperty("data").GetProperty("posts")[0];
        Assert.Equal("Revised words.", edited.GetProperty("text").GetString());
        Assert.NotNull(edited.GetProperty("editedAt"));

        using (var delete = await app.Http.DeleteAsync($"/api/v1/experience/posts/{postId}", TestContext.Current.CancellationToken))
        {
            delete.EnsureSuccessStatusCode();
        }
        var after = await ReadTopic(app.Http, topicRef);
        Assert.True(after.GetProperty("result").GetProperty("data").GetProperty("posts")[0].GetProperty("removed").GetBoolean());
    }

    [Fact]
    public async Task A_participant_read_answers_with_profile_and_read_filtered_activity()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        using var response = await app.Http.GetAsync(
            $"/api/v1/experience/participants/{ExperienceWebApp.HumanDid}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var data = (await ReadAsync(response)).GetProperty("result").GetProperty("data");
        Assert.Equal("leo.experience.test", data.GetProperty("profile").GetProperty("handle").GetString());
        Assert.True(data.GetProperty("recentPosts").GetArrayLength() >= 1);
        Assert.All(data.GetProperty("recentPosts").EnumerateArray(), post => Assert.False(post.GetProperty("removed").GetBoolean()));
    }

    [Fact]
    public async Task Own_writes_settle_into_the_updates_feed_and_clear_on_acknowledgement()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        var started = await app.Http.PostAsJsonAsync("/api/v1/experience/tangents/proj/topics",
            new { requestId = "thread-settle-1", title = "Settlement", text = "A write that must settle." }, TestContext.Current.CancellationToken);
        started.EnsureSuccessStatusCode();
        var topicRef = (await ReadAsync(started)).GetProperty("result").GetProperty("data").GetProperty("topicRef").GetString()!;
        var topicKey = topicRef.Split("::")[^1];

        // Scoped to the new Topic: its opening Post is the one write awaiting settlement.
        using var updates = await app.Http.GetAsync(
            "/api/v1/experience/updates?scopeRef=" + Uri.EscapeDataString(topicRef), TestContext.Current.CancellationToken);
        updates.EnsureSuccessStatusCode();
        var items = (await ReadAsync(updates)).GetProperty("attention").GetProperty("items").EnumerateArray().ToArray();
        var settlement = Assert.Single(items, item => item.GetProperty("kind").GetString() == "write_settlement");
        Assert.Equal("A write that must settle.", settlement.GetProperty("excerpt").GetString());

        using var window = await app.Http.GetAsync($"/api/v1/experience/topics/{topicKey}", TestContext.Current.CancellationToken);
        window.EnsureSuccessStatusCode();
        var readCursor = (await ReadAsync(window)).GetProperty("continuation").GetProperty("readCursor").GetString()!;
        using var acknowledge = await app.Http.PostAsJsonAsync($"/api/v1/experience/topics/{topicKey}/read-position",
            new { readCursor }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, acknowledge.StatusCode);

        using var after = await app.Http.GetAsync(
            "/api/v1/experience/updates?scopeRef=" + Uri.EscapeDataString(topicRef), TestContext.Current.CancellationToken);
        after.EnsureSuccessStatusCode();
        Assert.DoesNotContain((await ReadAsync(after)).GetProperty("attention").GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == "write_settlement");
    }

    /// <summary>The one place a double costs something visible is the post, so the post itself
    /// carries the idempotency: the same requestId replays the row it created (ADR 0007) and a
    /// different text under it is the action's own conflict. No infrastructure, no protocol.</summary>
    [Fact]
    public async Task A_post_requestId_replays_its_row_and_conflicts_on_divergence()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        var body = new { requestId = "post-once-1", text = "Exactly once." };
        using var first = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/posts", body, TestContext.Current.CancellationToken);
        first.EnsureSuccessStatusCode();
        var postRef = (await ReadAsync(first)).GetProperty("result").GetProperty("data").GetProperty("postRef").GetString()!;

        using var replay = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/posts", body, TestContext.Current.CancellationToken);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(postRef, (await ReadAsync(replay)).GetProperty("result").GetProperty("data").GetProperty("postRef").GetString());

        using var divergent = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/posts",
            new { requestId = "post-once-1", text = "Different words, same id." }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, divergent.StatusCode);
    }

    private static async Task<JsonElement> ReadTopic(HttpClient http, string topicRef)
    {
        var topicKey = topicRef.Split("::")[^1];
        using var response = await http.GetAsync($"/api/v1/experience/topics/{topicKey}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAsync(response);
    }

    /// <summary>Files a steward-readable case on the human's post: the report is reporter-safe,
    /// so the case ref arrives through the case list, exactly like a real steward's path.</summary>
    private static async Task<string> ReportHumanPostAsOwner(ExperienceWebApp app)
    {
        using var report = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/reports",
            new { requestId = "case-ground-1", postRef = $"{app.Origin}::proj::proj-z::m-leo-r1",
                reasonCode = "conduct.tone", statement = "Inspect the tone." }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        using var cases = await app.Http.GetAsync("/api/v1/experience/topics/proj-z/moderation/cases", TestContext.Current.CancellationToken);
        cases.EnsureSuccessStatusCode();
        var list = await ReadAsync(cases);
        return list.GetProperty("result").GetProperty("data").GetProperty("cases")[0].GetProperty("caseRef").GetString()!;
    }

    private static async Task<(string Token, string CredentialId)> IssueHumanCredential(ExperienceWebApp app)
    {
        var (credential, token) = ParticipantCredential.Issue(app.HumanParticipantId, "Integration human", 1,
            [ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post], DateTimeOffset.UtcNow);
        using (EntityContext.NoCache())
            await credential.Save(TestContext.Current.CancellationToken);
        return (token, credential.Id);
    }
}
