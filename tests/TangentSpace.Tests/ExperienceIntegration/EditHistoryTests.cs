using System.Net.Http.Json;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using TangentSpace.Conversation;
using TangentSpace.Participation;
using Xunit;

namespace TangentSpace.Tests.ExperienceIntegration;

/// <summary>Edit-history changelog (W1-B) against the real web application: pre-edit snapshots
/// on every applied change, facets riding their era, deterministic re-detection, idempotent
/// re-delivery, facet-payload conflicts, and the changelog-only read-only history surface with
/// its author/moderator row gate.</summary>
[Xunit.Collection("Experience integration")]
public sealed class EditHistoryTests : IAsyncLifetime
{
    private ExperienceWebApp app = null!;
    private HttpClient moderator = null!;
    private string moderatorToken = "";

    public async ValueTask InitializeAsync()
    {
        app = await ExperienceWebApp.StartAsync();
        // The seeded owner credential carries no manage grant; settings changes and moderation
        // removal both need one.
        moderatorToken = await MintModeratorToken();
        moderator = new HttpClient { BaseAddress = new Uri(app.Origin) };
        moderator.DefaultRequestHeaders.Authorization = new("Bearer", moderatorToken);
        // Editing is disabled by default; the seeded Topic opts in once for this suite.
        using var settings = await moderator.PatchAsJsonAsync($"/api/rooms/{ExperienceWebApp.TopicKey}/settings",
            new { allowPostEditing = true, isLocked = false });
        settings.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        moderator.Dispose();
        await app.DisposeAsync();
    }

    private async Task<string> MintModeratorToken()
    {
        var clock = app.Services.GetRequiredService<TimeProvider>();
        var (credential, token) = ParticipantCredential.Issue(app.OwnerParticipantId, "History moderation test", 1,
            [ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post, ParticipationGrants.Manage],
            clock.GetUtcNow(), managementPermitted: true);
        using var fresh = EntityContext.NoCache();
        await credential.Save();
        return token;
    }

    private async Task<string> Post(string text, object? facets = null)
    {
        using var response = await app.Http.PostAsJsonAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/posts",
            new { requestId = "hist-" + Guid.CreateVersion7().ToString("n")[..12], text, facets });
        response.EnsureSuccessStatusCode();
        return text;
    }

    private async Task<JsonElement> LiveMessage(string text)
    {
        using var response = await app.Http.GetAsync($"/api/rooms/{ExperienceWebApp.TopicKey}/messages?from=start");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement.Clone();
        Assert.True(TryGet(root, "messages", out var messages) && messages.ValueKind == JsonValueKind.Array);
        var matches = messages.EnumerateArray().Where(message =>
            TryGet(message, "content", out var content) && TryGet(content, "text", out var value)
            && value.GetString() == text).ToList();
        Assert.Single(matches);
        return matches[0];
    }

    private async Task<HttpResponseMessage> Edit(string messageId, string text, string operationId, object? facets = null)
    {
        object body = facets is null ? new { text, operationId } : new { text, operationId, facets };
        return await app.Http.PatchAsJsonAsync($"/api/rooms/{ExperienceWebApp.TopicKey}/messages/{messageId}", body);
    }

    private async Task<HttpResponseMessage> Delete(HttpClient client, string messageId, string operationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/rooms/{ExperienceWebApp.TopicKey}/messages/{messageId}")
        {
            Content = JsonContent.Create(new { operationId }),
        };
        return await client.SendAsync(request);
    }

    /// <summary>The changelog rows visible to one client (each row carries ofMessageId,
    /// previousChangeId and changeClass on top of the copied pre-edit fields).</summary>
    private static async Task<List<JsonElement>> History(HttpClient client, string query = "set=changelog")
    {
        using var response = await client.GetAsync($"/api/history/messages?{query}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(System.Net.HttpStatusCode.OK == response.StatusCode, $"History read failed: {(int)response.StatusCode} {body}");
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement.Clone();
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        return root.EnumerateArray().ToList();
    }

    private static async Task<List<JsonElement>> HistoryOf(HttpClient client, string messageId)
    {
        var rows = await History(client);
        return rows.Where(row => TryGet(row, "ofMessageId", out var of) && of.GetString() == messageId).ToList();
    }

    internal static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        value = default;
        return false;
    }

    /// <summary>The serializers omit null members, so "null" means absent-or-null.</summary>
    internal static bool AbsentOrNull(JsonElement element, string name)
        => !TryGet(element, name, out var value) || value.ValueKind is JsonValueKind.Null;

    [Fact]
    public async Task An_edit_snapshots_the_pre_edit_row_and_moves_the_live_pointer()
    {
        var text = await Post("history-first-original");
        var live = await LiveMessage(text);
        var messageId = Get(live, "id").GetString()!;

        using var edit = await Edit(messageId, "history-first-edited", "edit-op-first");
        Assert.Equal(System.Net.HttpStatusCode.OK, edit.StatusCode);

        var rows = await HistoryOf(app.Http, messageId);
        var row = Assert.Single(rows);
        Assert.Equal("history-first-original", Get(Get(row, "content"), "text").GetString());
        Assert.Equal(app.AgentParticipantId, Get(row, "authorParticipantId").GetString());
        Assert.True(AbsentOrNull(row, "previousChangeId"));
        var classification = Get(row, "changeClass");
        Assert.True(classification.ValueKind is JsonValueKind.Object);
        Assert.True(TryGet(classification, "surfaceDistance", out var surface) && surface.GetDouble() >= 0);
        Assert.True(TryGet(classification, "tokenOverlap", out var overlap) && overlap.GetDouble() >= 0);
        // The configured in-process embedder is active in this host, so the semantic axis carries
        // a real value and the classifier names the model instead of an inactive marker.
        Assert.True(TryGet(classification, "semanticDistance", out var semantic)
            && semantic.ValueKind is JsonValueKind.Number && semantic.GetDouble() >= 0);
        Assert.Contains("onnx:", Get(classification, "classifier").GetString());

        var updated = await LiveMessage("history-first-edited");
        Assert.Equal(Get(row, "id").GetString(), Get(updated, "changeId").GetString());
    }

    [Fact]
    public async Task A_second_edit_chains_the_snapshot_link()
    {
        var text = await Post("history-chain-original");
        var messageId = Get(await LiveMessage(text), "id").GetString()!;
        await Edit(messageId, "history-chain-middle", "edit-op-chain-1");
        await Edit(messageId, "history-chain-final", "edit-op-chain-2");

        var rows = await HistoryOf(app.Http, messageId);
        Assert.Equal(2, rows.Count);
        var newest = rows.Single(row => Get(Get(row, "content"), "text").GetString() == "history-chain-middle");
        var oldest = rows.Single(row => Get(Get(row, "content"), "text").GetString() == "history-chain-original");
        Assert.Equal(Get(oldest, "id").GetString(), Get(newest, "previousChangeId").GetString());
        Assert.True(AbsentOrNull(oldest, "previousChangeId"));
        var live = await LiveMessage("history-chain-final");
        Assert.Equal(Get(newest, "id").GetString(), Get(live, "changeId").GetString());
    }

    [Fact]
    public async Task Facets_ride_their_era()
    {
        var text = "history-era-original";
        await Post(text, new[] { new { kind = "mention", start = 0, end = 4, did = ExperienceWebApp.HumanDid } });
        var messageId = Get(await LiveMessage(text), "id").GetString()!;

        var replacement = "history-era-edited";
        var start = System.Text.Encoding.UTF8.GetByteCount(replacement);
        using var edit = await Edit(messageId, replacement, "edit-op-era",
            new[] { new { kind = "mention", start = 0, end = start, did = ExperienceWebApp.AgentDid } });
        edit.EnsureSuccessStatusCode();

        var snapshot = Assert.Single(await HistoryOf(app.Http, messageId));
        var snapshotFacets = Get(snapshot, "facets");
        Assert.True(snapshotFacets.ValueKind is JsonValueKind.Array && snapshotFacets.GetArrayLength() == 1);
        Assert.Equal(ExperienceWebApp.HumanDid, Get(snapshotFacets[0], "did").GetString());
        Assert.Equal(0, Get(snapshotFacets[0], "start").GetInt32());
        Assert.Equal(4, Get(snapshotFacets[0], "end").GetInt32());

        var live = await LiveMessage(replacement);
        var liveFacets = Get(live, "facets");
        Assert.Equal(ExperienceWebApp.AgentDid, Get(liveFacets[0], "did").GetString());
        Assert.Equal(start, Get(liveFacets[0], "end").GetInt32());
    }

    [Fact]
    public async Task Provided_facets_are_stored_verbatim_on_the_new_live_content()
    {
        var text = await Post("history-provided-original");
        var messageId = Get(await LiveMessage(text), "id").GetString()!;
        var edited = "history-provided-edited";
        var end = System.Text.Encoding.UTF8.GetByteCount(edited);
        await Edit(messageId, edited, "edit-op-provided",
            new[] { new { kind = "tag", start = 0, end = end - 8, value = "plan" } });

        var live = await LiveMessage(edited);
        var facets = Get(live, "facets");
        Assert.Equal(1, facets.GetArrayLength());
        Assert.Equal("tag", Get(facets[0], "kind").GetString());
        Assert.Equal("plan", Get(facets[0], "value").GetString());
    }

    [Fact]
    public async Task Absent_facets_re_detect_mentions_groups_and_fence_rules_deterministically()
    {
        var text = await Post("history-detect-original");
        var messageId = Get(await LiveMessage(text), "id").GetString()!;

        var edited = "Ping @leo.experience.test please.";
        await Edit(messageId, edited, "edit-op-detect-1");
        var live = await LiveMessage(edited);
        var facets = Get(live, "facets");
        Assert.Equal(1, facets.GetArrayLength());
        Assert.Equal("mention", Get(facets[0], "kind").GetString());
        Assert.Equal(ExperienceWebApp.HumanDid, Get(facets[0], "did").GetString());
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount("Ping "), Get(facets[0], "start").GetInt32());
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount("Ping @leo.experience.test"), Get(facets[0], "end").GetInt32());

        // Same text again: re-detection is deterministic, so the replay does not conflict.
        await Edit(messageId, edited, "edit-op-detect-2");
        var repeated = await LiveMessage(edited);
        Assert.Equal(Get(facets[0], "start").GetInt32(), Get(Get(repeated, "facets")[0], "start").GetInt32());

        var grouped = "@admins the plan changed.";
        await Edit(messageId, grouped, "edit-op-detect-3");
        var groupFacets = Get(await LiveMessage(grouped), "facets");
        Assert.Equal(1, groupFacets.GetArrayLength());
        Assert.Equal("group", Get(groupFacets[0], "kind").GetString());
        Assert.Equal("admins", Get(groupFacets[0], "value").GetString());

        var fenced = "```\n@leo.experience.test\n```\nDone.";
        await Edit(messageId, fenced, "edit-op-detect-4");
        Assert.True(AbsentOrNull(await LiveMessage(fenced), "facets"));
    }

    [Fact]
    public async Task Author_delete_snapshots_the_pre_delete_row()
    {
        var text = await Post("history-delete-original");
        var live = Get(await LiveMessage(text), "id").GetString()!;
        using var deleted = await Delete(app.Http, live, "edit-op-delete");
        Assert.Equal(System.Net.HttpStatusCode.OK, deleted.StatusCode);

        var snapshot = Assert.Single(await HistoryOf(app.Http, live));
        Assert.Equal(text, Get(Get(snapshot, "content"), "text").GetString());
        Assert.True(TryGet(snapshot, "removed", out var removed) && removed.GetBoolean() is false);
        var updated = await MessageRow(live);
        Assert.True(Get(updated, "removed").GetBoolean());
        Assert.Equal(Get(snapshot, "id").GetString(), Get(updated, "changeId").GetString());
    }

    [Fact]
    public async Task Moderation_removal_snapshots_too()
    {
        var text = await Post("history-moderated-original");
        var messageId = Get(await LiveMessage(text), "id").GetString()!;
        using var removed = await Delete(moderator, messageId, "edit-op-moderate");
        Assert.Equal(System.Net.HttpStatusCode.OK, removed.StatusCode);
        Assert.Contains("moderated", await removed.Content.ReadAsStringAsync());

        var snapshot = Assert.Single(await HistoryOf(app.Http, messageId));
        Assert.Equal(text, Get(Get(snapshot, "content"), "text").GetString());
        Assert.Equal(app.AgentParticipantId, Get(snapshot, "authorParticipantId").GetString());
        Assert.True(Get(await MessageRow(messageId), "removed").GetBoolean());
    }

    [Fact]
    public async Task A_re_delivered_edit_adds_no_second_snapshot_and_returns_the_same_receipt()
    {
        var text = await Post("history-replay-original");
        var messageId = Get(await LiveMessage(text), "id").GetString()!;
        using var first = await Edit(messageId, "history-replay-edited", "edit-op-replay");
        Assert.Equal(System.Net.HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync();

        using var replay = await Edit(messageId, "history-replay-edited", "edit-op-replay");
        Assert.Equal(System.Net.HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(firstBody, await replay.Content.ReadAsStringAsync());

        Assert.Single(await HistoryOf(app.Http, messageId));
    }

    [Fact]
    public async Task Replaying_an_older_terminal_edit_after_a_later_edit_returns_its_receipt()
    {
        // The ledger remembers what each operation delivered, so an old completed operation
        // replays its own receipt even after a newer edit replaced the live content.
        var text = await Post("history-stale-original");
        var messageId = Get(await LiveMessage(text), "id").GetString()!;
        using var first = await Edit(messageId, "history-stale-middle", "edit-op-stale-1");
        var firstBody = await first.Content.ReadAsStringAsync();
        using var later = await Edit(messageId, "history-stale-final", "edit-op-stale-2");
        later.EnsureSuccessStatusCode();

        using var replay = await Edit(messageId, "history-stale-middle", "edit-op-stale-1");
        Assert.Equal(System.Net.HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(firstBody, await replay.Content.ReadAsStringAsync());
        Assert.Equal(2, (await HistoryOf(app.Http, messageId)).Count);
    }

    [Fact]
    public async Task A_pending_local_edit_retry_completes()
    {
        // The crash window between ledger creation and application: the operation row exists,
        // still pending, and nothing was applied. Re-delivering the same package completes it —
        // the conflict check reads the ledger's remembered payload, not the pre-edit live row.
        var text = await Post("history-pending-original");
        var messageId = Get(await LiveMessage(text), "id").GetString()!;
        var edited = "history-pending-edited";
        const string operationId = "edit-op-pending";
        await SavePendingLedger(messageId, operationId, delete: false, edited);

        using var retried = await Edit(messageId, edited, operationId);
        Assert.Equal(System.Net.HttpStatusCode.OK, retried.StatusCode);
        using var document = JsonDocument.Parse(await retried.Content.ReadAsStringAsync());
        Assert.Equal("accepted", document.RootElement.GetProperty("state").GetString());
        Assert.Equal(edited, Get(Get(await LiveMessage(edited), "content"), "text").GetString());
        Assert.Single(await HistoryOf(app.Http, messageId));
    }

    [Fact]
    public async Task A_pending_local_delete_of_a_faceted_post_retries_to_completion()
    {
        var text = "history-pending-delete";
        await Post(text, new[] { new { kind = "mention", start = 0, end = 4, did = ExperienceWebApp.HumanDid } });
        var messageId = Get(await LiveMessage(text), "id").GetString()!;
        const string operationId = "edit-op-pending-delete";
        await SavePendingLedger(messageId, operationId, delete: true, null);

        using var retried = await Delete(app.Http, messageId, operationId);
        Assert.Equal(System.Net.HttpStatusCode.OK, retried.StatusCode);
        using var document = JsonDocument.Parse(await retried.Content.ReadAsStringAsync());
        Assert.Equal("deleted", document.RootElement.GetProperty("state").GetString());
        Assert.True(Get(await MessageRow(messageId), "removed").GetBoolean());
        Assert.Single(await HistoryOf(app.Http, messageId));
    }

    /// <summary>The durable pending operation row exactly as the crash window leaves it.</summary>
    private async Task SavePendingLedger(string messageId, string operationId, bool delete, string? text)
    {
        var ledger = new PostChange
        {
            Id = PostChange.Key(ExperienceWebApp.AgentDid, ExperienceWebApp.TopicKey, messageId, operationId),
            RoomKey = ExperienceWebApp.TopicKey, MessageId = messageId, ActorParticipantId = ExperienceWebApp.AgentDid,
            OperationId = operationId, Delete = delete, Text = text, State = "pending",
            Detail = "source-change-failed-retry-same-operation", UpdatedAt = DateTimeOffset.UtcNow,
        };
        using var fresh = EntityContext.NoCache();
        await ledger.Save();
    }

    [Fact]
    public async Task Multi_line_re_detection_uses_absolute_byte_offsets()
    {
        var text = await Post("history-multiline-original");
        var messageId = Get(await LiveMessage(text), "id").GetString()!;
        var edited = "hey @owner.experience.test\nalso @agent.experience.test please";
        using var edit = await Edit(messageId, edited, "edit-op-multiline");
        edit.EnsureSuccessStatusCode();

        // Expected offsets computed from the text's own segments: line one's mention, then line
        // two's mention shifted by the full first line plus its one-byte newline separator.
        int Bytes(string value) => System.Text.Encoding.UTF8.GetByteCount(value);
        var ownerStart = Bytes("hey ");
        var ownerEnd = ownerStart + Bytes("@owner.experience.test");
        var secondLine = Bytes("hey @owner.experience.test") + 1;
        var agentStart = secondLine + Bytes("also ");
        var agentEnd = agentStart + Bytes("@agent.experience.test");

        var facets = Get(await LiveMessage(edited), "facets");
        Assert.Equal(2, facets.GetArrayLength());
        var owner = facets[0];
        Assert.Equal("mention", Get(owner, "kind").GetString());
        Assert.Equal(ExperienceWebApp.OwnerDid, Get(owner, "did").GetString());
        Assert.Equal(ownerStart, Get(owner, "start").GetInt32());
        Assert.Equal(ownerEnd, Get(owner, "end").GetInt32());
        var agent = facets[1];
        Assert.Equal("mention", Get(agent, "kind").GetString());
        Assert.Equal(ExperienceWebApp.AgentDid, Get(agent, "did").GetString());
        Assert.Equal(agentStart, Get(agent, "start").GetInt32());
        Assert.Equal(agentEnd, Get(agent, "end").GetInt32());
        // Non-overlapping absolute ranges (a line-relative bug collides them or trips the check).
        Assert.True(Get(owner, "end").GetInt32() <= Get(agent, "start").GetInt32());
    }

    [Fact]
    public async Task The_same_operation_key_with_different_facets_conflicts()
    {
        var text = await Post("history-conflict-original");
        var messageId = Get(await LiveMessage(text), "id").GetString()!;
        var edited = "history-conflict-edited";
        await Edit(messageId, edited, "edit-op-conflict",
            new[] { new { kind = "mention", start = 0, end = 4, did = ExperienceWebApp.HumanDid } });
        using var conflicting = await Edit(messageId, edited, "edit-op-conflict",
            new[] { new { kind = "mention", start = 0, end = 4, did = ExperienceWebApp.AgentDid } });
        Assert.Equal(System.Net.HttpStatusCode.Conflict, conflicting.StatusCode);
        Assert.Single(await HistoryOf(app.Http, messageId));
    }

    [Fact]
    public async Task History_reads_gate_by_author_moderator_and_identity()
    {
        var agentText = await Post("history-gate-agent");
        var agentMessage = Get(await LiveMessage(agentText), "id").GetString()!;
        await Edit(agentMessage, "history-gate-agent-edited", "edit-op-gate-agent");

        var ownerText = "history-gate-owner";
        using var ownerClient = NewOwnerClient();
        using var posted = await ownerClient.PostAsJsonAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/posts",
            new { requestId = "hist-gate-owner", text = ownerText });
        posted.EnsureSuccessStatusCode();
        var ownerMessage = Get(await LiveMessage(ownerText), "id").GetString()!;
        using var ownerEdit = await ownerClient.PatchAsJsonAsync($"/api/rooms/{ExperienceWebApp.TopicKey}/messages/{ownerMessage}",
            new { text = "history-gate-owner-edited", operationId = "edit-op-gate-owner" });
        ownerEdit.EnsureSuccessStatusCode();

        // The author sees their own snapshot; the moderator sees the room's; the plain member
        // sees nothing of the owner's; the anonymous caller is denied at the door.
        var agentRows = await History(app.Http);
        Assert.Contains(agentRows, row => Get(row, "ofMessageId").GetString() == agentMessage);
        Assert.DoesNotContain(agentRows, row => Get(row, "ofMessageId").GetString() == ownerMessage);

        var moderatorRows = await History(moderator);
        Assert.Contains(moderatorRows, row => Get(row, "ofMessageId").GetString() == agentMessage);
        Assert.Contains(moderatorRows, row => Get(row, "ofMessageId").GetString() == ownerMessage);

        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        using var denied = await anonymous.GetAsync("/api/history/messages?set=changelog");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task The_history_surface_denies_other_sets_and_every_write_shape()
    {
        using var none = await app.Http.GetAsync("/api/history/messages");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, none.StatusCode);
        using var wrong = await app.Http.GetAsync("/api/history/messages?set=default");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, wrong.StatusCode);
        using var multi = await app.Http.GetAsync("/api/history/messages?set=changelog&set=default");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, multi.StatusCode);

        // Body-carrying writes can also be refused by [ApiController] binding validation before
        // the action runs; either way the write is denied (never 2xx).
        using var post = await app.Http.PostAsJsonAsync("/api/history/messages?set=changelog", new { id = "x", roomKey = "y" });
        Assert.True(post.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest,
            $"POST denied unexpectedly: {(int)post.StatusCode}");
        using var postBodySet = await app.Http.PostAsJsonAsync("/api/history/messages", new { set = "changelog", id = "x" });
        Assert.True(postBodySet.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest,
            $"body-set POST denied unexpectedly: {(int)postBodySet.StatusCode}");
        using var put = await app.Http.PutAsJsonAsync("/api/history/messages/x?set=changelog", new { id = "x" });
        Assert.True(put.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest,
            $"PUT denied unexpectedly: {(int)put.StatusCode}");
        using var patch = await app.Http.PatchAsJsonAsync("/api/history/messages/x?set=changelog", new { text = "no" });
        Assert.True(patch.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest,
            $"PATCH denied unexpectedly: {(int)patch.StatusCode}");
        using var removed = await app.Http.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
            "/api/history/messages/x?set=changelog")
        {
            Content = JsonContent.Create(new { operationId = "edit-op-deny" }),
        });
        Assert.True(removed.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest,
            $"DELETE denied unexpectedly: {(int)removed.StatusCode} {await removed.Content.ReadAsStringAsync()}");
        using var removeAll = await app.Http.DeleteAsync("/api/history/messages/all?set=changelog");
        Assert.True(removeAll.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest,
            $"DELETE-all denied unexpectedly: {(int)removeAll.StatusCode}");
        using var bulk = await app.Http.PostAsJsonAsync("/api/history/messages/bulk?set=changelog",
            new[] { new { id = "x", roomKey = "y" } });
        Assert.True(bulk.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest,
            $"bulk POST denied unexpectedly: {(int)bulk.StatusCode}");
        using var query = await app.Http.PostAsJsonAsync("/api/history/messages/query", new { set = "changelog" });
        Assert.True(query.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.BadRequest,
            $"query POST denied unexpectedly: {(int)query.StatusCode} {await query.Content.ReadAsStringAsync()}");
        using var fresh = await app.Http.GetAsync("/api/history/messages/new");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, fresh.StatusCode);

        // The surface still serves the honest read: this author's own fresh snapshot.
        var text = await Post("history-surface-readable");
        var messageId = Get(await LiveMessage(text), "id").GetString()!;
        await Edit(messageId, "history-surface-readable-edited", "edit-op-surface");
        var rows = await History(app.Http);
        Assert.Contains(rows, row => Get(row, "ofMessageId").GetString() == messageId);
    }

    [Fact]
    public async Task Filters_cannot_bypass_the_row_gate()
    {
        var agentText = await Post("history-filter-agent");
        var agentMessage = Get(await LiveMessage(agentText), "id").GetString()!;
        await Edit(agentMessage, "history-filter-agent-edited", "edit-op-filter-agent");

        // A member asks to see an author's rows they could never see: the row gate still applies
        // server-side, and the same filter stays honest in a moderator's hands.
        var escape = Uri.EscapeDataString($"{{\"AuthorParticipantId\":{{\"$eq\":\"{app.OwnerParticipantId}\"}}}}");
        using var ownerClient = NewOwnerClient();
        using var posted = await ownerClient.PostAsJsonAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/posts",
            new { requestId = "hist-filter-owner", text = "history-filter-owner" });
        posted.EnsureSuccessStatusCode();
        var ownerMessage = Get(await LiveMessage("history-filter-owner"), "id").GetString()!;
        using var ownerEdit = await ownerClient.PatchAsJsonAsync($"/api/rooms/{ExperienceWebApp.TopicKey}/messages/{ownerMessage}",
            new { text = "history-filter-owner-edited", operationId = "edit-op-filter-owner" });
        ownerEdit.EnsureSuccessStatusCode();

        var agentRows = await History(app.Http, $"set=changelog&filter={escape}");
        Assert.DoesNotContain(agentRows, row => Get(row, "ofMessageId").GetString() == ownerMessage);

        var moderatorRows = await History(moderator, $"set=changelog&filter={escape}");
        Assert.Contains(moderatorRows, row => Get(row, "ofMessageId").GetString() == ownerMessage);
    }

    private HttpClient NewOwnerClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(app.Origin) };
        client.DefaultRequestHeaders.Authorization = new("Bearer", app.OwnerToken);
        return client;
    }

    /// <summary>The stored live row (partition default) rather than the projection above.</summary>
    private async Task<JsonElement> MessageRow(string messageId)
    {
        using var fresh = EntityContext.NoCache();
        var message = await Message.Get(messageId);
        Assert.NotNull(message);
        return JsonSerializer.SerializeToElement(message, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    internal static JsonElement Get(JsonElement element, string name)
    {
        Assert.True(TryGet(element, name, out var value), $"Expected property '{name}' on {element}.");
        return value;
    }
}
