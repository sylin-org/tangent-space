using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
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
        // A source author that never signed in here still mints a participant through the same
        // Enroll path, so ledger and projection references are participant ids.
        var author = await directory.EnsureAtproto(source.Author, ct);
        var remaining = source.Records.OrderBy(r => r.Collection, StringComparer.Ordinal).ThenBy(r => r.RecordKey, StringComparer.Ordinal).ToList();
        while (remaining.Count > 0)
        {
          var deferred = new List<VerifiedRecord>();
          foreach (var record in remaining)
          {
            var uri = source.Space + "/" + source.Author + "/" + record.Collection + "/" + record.RecordKey;
            var id = SourceDecision.Key(roomKey, uri, record.Cid);
            var accepted = false;
            var pending = await governance.WithCurrentPolicy(author.Id, roomKey, async (policy, token) =>
            {
                if (policy.SpaceUri != source.Space) throw new InvalidDataException("Source Space differs from current room mapping.");
                var previous = await SourceDecision.Get(id, token);
                if (previous is not null)
                {
                    // A missing version projection must never resurrect a deleted/tombstoned
                    // post under that version's CID. Rebuild handles legitimate gaps explicitly.
                    return false;
                }
                var current = (await Message.Query(m => m.RoomKey == roomKey && m.SourceUri == uri,
                    Window<Message>(nameof(Message.Sequence), 1, 2), token)).FirstOrDefault();
                MessageContent? content = null;
                var reason = policy.CanWrite ? "accepted" : policy.Reason == "allowed" ? "read-only" : policy.Reason;
                if (record.Collection != SpacesOptions.Collection) reason = "unsupported-collection";
                else
                {
                    try { content = MessageContent.FromCbor(record.DagCbor); }
                    catch (Exception e) when (e is InvalidDataException or ArgumentException or System.Formats.Cbor.CborContentException or KeyNotFoundException or InvalidOperationException)
                    { reason = "invalid-record"; }
                }
                // Source versions are write-once decisions. A participant may only advance
                // the displayed version when room editing is enabled, except for the CID
                // already committed by our own native edit operation.
                if (reason == "accepted" && current is not null && current.SourceCid != record.Cid && !policy.EditingAllowed)
                    reason = "editing-not-allowed";
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
                    Id = id, RoomKey = roomKey, AuthorParticipantId = author.Id, SourceUri = uri, SourceCid = record.Cid,
                    Accepted = reason == "accepted", Reason = reason, DecidedAt = clock.GetUtcNow(),
                    PolicyRevision = policy.SelectedPolicyRevision, SitePolicyRevision = policy.SitePolicyRevision,
                    Content = reason == "accepted" ? content : null,
                    Sequence = reason == "accepted" ? current?.Sequence ?? checked(++state.LastSequence) : 0
                };
                await decision.Save(token);
                if (decision.Accepted)
                {
                    // A source update is a new ledger decision but the displayed post keeps
                    // the identity assigned to its first accepted version. Moderation state
                    // belongs to that projection and survives re-ingestion.
                    if (current?.Removed == true) return false;
                    var projected = Message.Project(decision);
                    if (current is not null)
                    {
                        projected.Id = current.Id;
                        projected.Removed = current.Removed;
                        projected.RemovedAt = current.RemovedAt;
                        projected.RemovedByParticipantId = current.RemovedByParticipantId;
                        projected.EditedAt = current.EditedAt;
                        projected.Sequence = current.Sequence;
                        projected.AcceptedAt = current.AcceptedAt;
                        if (current.SourceCid != record.Cid) projected.EditedAt ??= decision.DecidedAt;
                    }
                    await projected.Save(token);
                    decision.Sequence = projected.Sequence;
                    await decision.Save(token);
                    await state.Save(token);
                    var room = await Room.Get(roomKey, token);
                    await ActivityJournal.AppendInTransaction(ActivityKind.MessageAccepted, roomKey, author.Id, null,
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

    public async Task<int> Rebuild(string actorId, string roomKey, CancellationToken ct)
    {
        // Keep the ledger and sequence assignment. Rebuild never re-admits old rejected versions.
        var rebuilt = 0;
        for (var page = 1; ; page++)
        {
            var decisions = await SourceDecision.Query(d => d.RoomKey == roomKey && d.Accepted,
                Window<SourceDecision>(nameof(SourceDecision.Sequence), page, 100), ct);
            await governance.WithCurrentPolicy(actorId, roomKey, async (policy, token) =>
            {
                if (!policy.CanAppointManagers) throw new UnauthorizedAccessException("Only the owner can rebuild a room projection.");
                foreach (var decision in decisions)
                {
                    var current = (await Message.Query(m => m.RoomKey == roomKey && m.SourceUri == decision.SourceUri,
                        Window<Message>(nameof(Message.Sequence), 1, 2), token)).FirstOrDefault();
                    if (current?.Removed == true) continue;
                    if (current is not null) continue;
                    var latest = (await SourceDecision.Query(d => d.RoomKey == roomKey && d.SourceUri == decision.SourceUri && d.Accepted,
                        new QueryDefinition { Page = 1, PageSize = 1,
                            Sort = [new SortSpec(new MemberPath(typeof(SourceDecision), [typeof(SourceDecision).GetProperty(nameof(SourceDecision.DecidedAt))!], typeof(DateTimeOffset), false, -1), true)] }, token)).FirstOrDefault();
                    if (latest is null || latest.Id != decision.Id) continue;
                    var first = (await SourceDecision.Query(d => d.RoomKey == roomKey && d.SourceUri == decision.SourceUri && d.Accepted,
                        Window<SourceDecision>(nameof(SourceDecision.DecidedAt), 1, 1), token)).First();
                    var projected = Message.Project(decision);
                    projected.Id = first.Id; projected.Sequence = first.Sequence; projected.AcceptedAt = first.DecidedAt;
                    if (first.Id != decision.Id) projected.EditedAt = decision.DecidedAt;
                    await projected.Save(token); rebuilt++;
                }
                return true;
            }, ct);
            if (decisions.Count < 100) return rebuilt;
        }
    }
}
