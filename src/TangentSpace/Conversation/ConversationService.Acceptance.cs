using Koan.Data.Core;
using TangentSpace.AtProtocol;
using TangentSpace.AtProtocol.Verification;
using TangentSpace.Activity;
using TangentSpace.Rooms;

namespace TangentSpace.Conversation;

public sealed partial class ConversationService
{
    // Network verification has already bound this snapshot to the expected PDS, author and exact Space.
    private async Task<int> Accept(string roomKey, VerifiedSpaceRepo source, CancellationToken ct)
    {
        var remaining = source.Records.OrderBy(r => r.Collection, StringComparer.Ordinal).ThenBy(r => r.RecordKey, StringComparer.Ordinal).ToList();
        while (remaining.Count > 0)
        {
          var deferred = new List<VerifiedRecord>();
          foreach (var record in remaining)
          {
            var uri = source.Space + "/" + source.Author + "/" + record.Collection + "/" + record.RecordKey;
            var id = SourceDecision.Key(roomKey, uri, record.Cid);
            var accepted = false;
            var pending = await governance.WithCurrentPolicy(source.Author, roomKey, async (policy, token) =>
            {
                if (policy.SpaceUri != source.Space) throw new InvalidDataException("Source Space differs from current room mapping.");
                var previous = await SourceDecision.Get(id, token);
                if (previous is not null)
                {
                    if (previous.Accepted && await Message.Get(id, token) is null) await Message.Project(previous).Save(token);
                    return false;
                }
                MessageContent? content = null;
                var reason = policy.CanWrite ? "accepted" : policy.Reason == "allowed" ? "read-only" : policy.Reason;
                if (record.Collection != SpacesOptions.Collection) reason = "unsupported-collection";
                else
                {
                    try { content = MessageContent.FromCbor(record.DagCbor); }
                    catch (Exception e) when (e is InvalidDataException or ArgumentException or System.Formats.Cbor.CborContentException or KeyNotFoundException or InvalidOperationException)
                    { reason = "invalid-record"; }
                }
                if (reason == "accepted" && content?.ReplyTo is { } reply)
                {
                    var target = await SourceDecision.Get(SourceDecision.Key(roomKey, reply.Uri, reply.Cid), token);
                    if (!reply.Uri.StartsWith(source.Space + "/", StringComparison.Ordinal) || target is { Accepted: false })
                        reason = "reply-not-accepted-in-room";
                    else if (target is null) return true; // Missing dependency is not a final policy decision. Recheck current policy when it arrives.
                }
                var state = await RoomConversation.Get(roomKey, token) ?? new RoomConversation { Id = roomKey };
                var decision = new SourceDecision
                {
                    Id = id, RoomKey = roomKey, AuthorDid = source.Author, SourceUri = uri, SourceCid = record.Cid,
                    Accepted = reason == "accepted", Reason = reason, DecidedAt = clock.GetUtcNow(),
                    PolicyRevision = policy.SelectedPolicyRevision, SitePolicyRevision = policy.SitePolicyRevision,
                    Content = reason == "accepted" ? content : null,
                    Sequence = reason == "accepted" ? checked(++state.LastSequence) : 0
                };
                await decision.Save(token);
                if (decision.Accepted)
                {
                    await Message.Project(decision).Save(token);
                    await state.Save(token);
                    var room = await Room.Get(roomKey, token);
                    await ActivityJournal.AppendInTransaction(ActivityKind.MessageAccepted, roomKey, source.Author, null,
                        room?.TangentKey, decision.Sequence, decision.DecidedAt, token);
                    accepted = true;
                }
                return false;
            }, ct);
            if (accepted)
            {
                // Both wake paths are after the coordinator's transaction commits.
                updates.Pulse(roomKey);
                ActivityJournal.SignalAfterCommit();
            }
            if (pending) deferred.Add(record);
          }
          if (deferred.Count == remaining.Count) return deferred.Count;
          remaining = deferred;
        }
        return 0;
    }

    public async Task<int> Rebuild(string actorDid, string roomKey, CancellationToken ct)
    {
        // Keep the ledger and sequence assignment. Rebuild never re-admits old rejected versions.
        var rebuilt = 0;
        for (var page = 1; ; page++)
        {
            var decisions = await SourceDecision.Query(d => d.RoomKey == roomKey && d.Accepted,
                Window<SourceDecision>(nameof(SourceDecision.Sequence), page, 100), ct);
            await governance.WithCurrentPolicy(actorDid, roomKey, async (policy, token) =>
            {
                if (!policy.CanAppointManagers) throw new UnauthorizedAccessException("Only the owner can rebuild a room projection.");
                foreach (var decision in decisions) { await Message.Project(decision).Save(token); rebuilt++; }
                return true;
            }, ct);
            if (decisions.Count < 100) return rebuilt;
        }
    }
}
