using Koan.Data.Abstractions;
using Koan.Data.Core;
using TangentSpace.AtProtocol;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using TangentSpace.Activity;

namespace TangentSpace.Conversation;

public sealed partial class ConversationService
{
    private int pendingPage = 1;
    public async Task<int> ReconcileWriter(string roomKey, string authorDid, CancellationToken ct)
    {
        await sync.WaitAsync(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            var room = await Room.Get(roomKey, ct);
            if (room?.SpaceState == RoomSpaceState.Local) return 0;
            if (room?.SpaceState != RoomSpaceState.Ready || room.SpaceUri is null) return 0;
            var pending = await Accept(roomKey, await spaces.ReadRepo(options.Value.AuthorityDid, room.SpaceUri, authorDid, ct), ct);
            // A checked writer is narrower evidence than a complete source sweep.
            await RecordFreshness(roomKey, pending == 0 ? "writer-checked" : "catching-up", ct);
            return pending;
        }
        finally { sync.Release(); }
    }

    public async Task<string> Reconcile(string? requesterDid, string roomKey, CancellationToken ct)
    {
        if (requesterDid is not null) await ReadPolicy(requesterDid, roomKey, ct);
        await sync.WaitAsync(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            var room = await Room.Get(roomKey, ct);
            if (room?.SpaceState == RoomSpaceState.Local) return "local";
            if (room?.SpaceState != RoomSpaceState.Ready) return "space-pending";
            var state = await RoomConversation.Get(roomKey, ct) ?? new RoomConversation { Id = roomKey };
            var result = "unavailable";
            var nextCursor = state.ReposCursor;
            var nextParticipantPage = state.ParticipantPage;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                var listed = await spaces.ListRepos(options.Value.AuthorityDid, room.SpaceUri!, state.ReposCursor, budget.Token);
                nextCursor = listed.TryGetProperty("cursor", out var cursor) ? cursor.GetString() : null;
                var writers = listed.GetProperty("repos").EnumerateArray().Select(r => r.GetProperty("did").GetString()!).ToHashSet(StringComparer.Ordinal);
                // Discover missed write notifications from known arrivals as well as the host's advertised writer set.
                var arrivals = await Participant.All(Window<Participant>(nameof(Participant.Id), state.ParticipantPage, 4), budget.Token);
                foreach (var participant in arrivals)
                    // Identity-only arrivals have no native writer to discover. Advertised source writers are still checked
                    // independently of their current consent, so grant changes never hide already-published source history.
                    if (!writers.Contains(participant.Id) && (await sourceReadiness.Get(participant.Id, roomKey, budget.Token)).CanWriteSource)
                        writers.Add(participant.Id);
                nextParticipantPage = arrivals.Count == 4 ? state.ParticipantPage + 1 : 1;
                var deferredReplies = 0;
                foreach (var author in writers.Order(StringComparer.Ordinal))
                {
                    try { deferredReplies += await Accept(roomKey, await spaces.ReadRepo(options.Value.AuthorityDid, room.SpaceUri!, author, budget.Token), budget.Token); }
                    catch (SpacesUnavailable absent) when (absent.Code == "RepoNotFound") { /* An arrival need not have authored in this room. */ }
                }
                result = nextCursor is null && nextParticipantPage == 1 && deferredReplies == 0 ? "checked" : "catching-up";
            }
            catch (Exception error) when (error is SpacesUnavailable or HttpRequestException or InvalidDataException or System.Text.Json.JsonException or OperationCanceledException)
            {
                ct.ThrowIfCancellationRequested();
                result = ConversationRecovery.RequiresReauthorization(error) ? "authority-reauthorization-required" : "unavailable";
                // Retry the same discovery window after a partial/outage failure.
                nextCursor = state.ReposCursor;
                nextParticipantPage = state.ParticipantPage;
            }
            await governance.WithCurrentPolicy(options.Value.AuthorityDid, roomKey, async (_, token) =>
            {
                var current = await RoomConversation.Get(roomKey, token) ?? new RoomConversation { Id = roomKey };
                current.LastAttemptAt = clock.GetUtcNow();
                var changed = current.Freshness != result;
                current.Freshness = result;
                current.ReposCursor = nextCursor;
                current.ParticipantPage = nextParticipantPage;
                if (result == "checked") current.LastCompleteAt = clock.GetUtcNow();
                await current.Save(token);
                if (changed) await ActivityJournal.AppendInTransaction(ActivityKind.SourceFreshnessChanged, roomKey,
                    tangentKey: room.TangentKey, occurredAt: clock.GetUtcNow(), ct: token);
                return true;
            }, ct);
            ActivityJournal.SignalAfterCommit();
            return result;
        }
        finally { sync.Release(); }
    }

    private async Task RecordFreshness(string roomKey, string value, CancellationToken ct)
    {
        var changed = await governance.WithCurrentPolicy(options.Value.AuthorityDid, roomKey, async (_, token) =>
        {
            var room = await Room.Get(roomKey, token);
            if (room is null) return false;
            var current = await RoomConversation.Get(roomKey, token) ?? new RoomConversation { Id = roomKey };
            var changed = current.Freshness != value;
            current.LastAttemptAt = clock.GetUtcNow();
            current.Freshness = value;
            await current.Save(token);
            if (changed) await ActivityJournal.AppendInTransaction(ActivityKind.SourceFreshnessChanged, roomKey,
                tangentKey: room.TangentKey, occurredAt: clock.GetUtcNow(), ct: token);
            return changed;
        }, ct);
        if (changed) ActivityJournal.SignalAfterCommit();
    }

    public async Task ReconcilePending(CancellationToken ct)
    {
        var pending = await WriteIntent.Query(i => i.State == "pending", Window<WriteIntent>(nameof(WriteIntent.Id), pendingPage, 8), ct);
        pendingPage = pending.Count < 8 ? 1 : pendingPage + 1;
        foreach (var intent in pending)
        {
            if (!ConversationRecovery.CanAttempt(intent, background: true)) continue;
            try { await Post(intent.AuthorDid, intent.RoomKey, new(intent.OperationId, intent.Content.Text, intent.Content.ReplyTo), ct, background: true); }
            catch (UnauthorizedAccessException) { /* Current policy denies retries; retain the intent for operator inspection. */ }
        }
    }
}
