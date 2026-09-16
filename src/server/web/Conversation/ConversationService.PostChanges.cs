using Koan.Data.Core;
using Koan.Data.Core.Model;
using TangentSpace.Activity;
using TangentSpace.Authorization;
using TangentSpace.Participants;
using TangentSpace.Rooms;

namespace TangentSpace.Conversation;

public sealed partial class ConversationService
{
    public async Task<PostChangeResult> ChangePost(string participantId, string roomKey, string messageId, string? text,
        bool delete, string operationId, CancellationToken ct, IReadOnlyList<PostFacet>? facets = null)
    {
        if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(operationId))
            throw new ArgumentException("A post ID and operation ID are required.");
        if (!delete)
        {
            if (text is null) throw new ArgumentException("Text is required for an edit.");
            PostContent.CheckText(text);
            PostFacets.Check(text, facets);
        }
        else if (facets is { Count: > 0 }) throw new ArgumentException("A removal cannot carry facets.");
        await writes.WaitAsync(ct);
        try
        {
            var change = await governance.WithCurrentPolicy(participantId, roomKey, async (policy, token) =>
            {
                var post = await ReadPostForChange(policy, roomKey, messageId, token);
                var id = PostChange.Key(participantId, roomKey, messageId, operationId);
                var previous = await PostChange.Get(id, token);
                if (previous is not null)
                {
                    if (previous.Delete != delete || previous.Text != text) throw new ArgumentException("That operation ID was already used with different content.");
                    // Facet conflict parity with create: the ledger remembers the client-sent facet
                    // payload, so the same package replays its receipt regardless of later edits.
                    // Re-detection is server-side derivation computed at application time; world
                    // state is not part of the delivered package (ADR 0007).
                    if (Canonical(previous.Facets) != Canonical(facets)) throw new WriteConflict();
                    if (previous.State is "accepted" or "deleted" or "moderated") return previous;
                }
                RequirePostChange(policy, post, delete);
                if (previous is not null) return previous;
                var created = new PostChange { Id = id, RoomKey = roomKey, MessageId = messageId, ActorParticipantId = participantId,
                    OperationId = operationId, Delete = delete, Text = text, Facets = facets, UpdatedAt = clock.GetUtcNow() };
                await created.Save(token);
                return created;
            }, ct);
            // Completed operations return before any mutation, so an idempotent re-delivery can never
            // mint a second snapshot.
            if (change.State is "accepted" or "moderated" or "deleted") return new(change.State, messageId, change.Detail);

            var post = await governance.WithCurrentPolicy(participantId, roomKey,
                (policy, token) => ReadPostForChange(policy, roomKey, messageId, token), ct);
            if (change.Delete && post.AuthorParticipantId != participantId)
            {
                await governance.WithCurrentPolicy(participantId, roomKey, async (currentPolicy, token) =>
                {
                    var current = await ReadPostForChange(currentPolicy, roomKey, messageId, token);
                    RequirePostChange(currentPolicy, current, delete: true);
                    await SnapshotChange(current, token);
                    current.Removed = true; current.Content = new PostContent("", current.Content.CreatedAt, current.Content.ReplyTo);
                    current.RemovedAt = clock.GetUtcNow(); current.RemovedByParticipantId = participantId;
                    // The words are gone; their byte ranges are meaningless on a removed row.
                    current.Facets = null;
                    await current.Save(token);
                    change.State = "moderated"; change.Detail = "post-removed-by-moderator"; change.UpdatedAt = clock.GetUtcNow();
                    var room = await Room.Get(roomKey, token);
                    await ActivityJournal.AppendInTransaction(ActivityKind.MessageDeleted, roomKey, participantId, current.AuthorParticipantId,
                        room?.TangentKey, current.Sequence, change.UpdatedAt, token);
                    await change.Save(token); return true;
                }, ct);
                updates.Pulse(roomKey); ActivityJournal.SignalAfterCommit();
                return new(change.State, messageId, change.Detail);
            }
            // An author's own edit or deletion changes the entity directly; authorship policy is unchanged.
            await governance.WithCurrentPolicy(participantId, roomKey, async (currentPolicy, token) =>
            {
                var current = await ReadPostForChange(currentPolicy, roomKey, messageId, token);
                RequirePostChange(currentPolicy, current, change.Delete);
                if (change.Delete)
                {
                    await SnapshotChange(current, token);
                    current.Removed = true;
                    current.Content = new PostContent("", current.Content.CreatedAt, current.Content.ReplyTo);
                    current.RemovedAt = clock.GetUtcNow();
                    current.RemovedByParticipantId = participantId;
                    current.Facets = null;
                }
                else
                {
                    // D2a: provided facets ride the new version; absent facets re-detect
                    // deterministically. The pre-edit structure rides its snapshot.
                    await SnapshotChange(current, token);
                    current.Content = new PostContent(change.Text!, current.Content.CreatedAt, current.Content.ReplyTo);
                    current.EditedAt = clock.GetUtcNow();
                    current.Facets = change.Facets;
                }
                await current.Save(token);
                change.State = change.Delete ? "deleted" : "accepted";
                change.Detail = change.Delete ? "post-deleted" : "post-edited";
                change.UpdatedAt = clock.GetUtcNow();
                var room = await Room.Get(roomKey, token);
                await ActivityJournal.AppendInTransaction(change.Delete ? ActivityKind.MessageDeleted : ActivityKind.MessageEdited,
                    roomKey, participantId, current.AuthorParticipantId, room?.TangentKey, current.Sequence, change.UpdatedAt, token);
                await change.Save(token);
                return true;
            }, ct);
            updates.Pulse(roomKey);
            ActivityJournal.SignalAfterCommit();
            return new(change.State, messageId, change.Detail);
        }
        finally { writes.Release(); }
    }

    /// <summary>Mint the pre-edit snapshot into the changelog partition and advance the live row's
    /// ChangeId pointer. Snapshots are write-once: this method is the only writer into the partition,
    /// every snapshot carries a fresh GUIDv7 identity, and an absence guard refuses any replay of a
    /// used identity. Must run inside the caller's governance transaction so the snapshot, the live
    /// mutation, the PostChange record and the activity journal commit together or not at all — a
    /// failure between the snapshot write and the live save rolls the transaction back.</summary>
    private static async Task SnapshotChange(Post live, CancellationToken token)
    {
        var snapshot = new Post
        {
            Id = Guid.CreateVersion7().ToString(),
            RoomKey = live.RoomKey, AuthorParticipantId = live.AuthorParticipantId, SourceUri = live.SourceUri, SourceCid = live.SourceCid,
            Sequence = live.Sequence, AcceptedAt = live.AcceptedAt, Content = live.Content, Removed = live.Removed,
            RemovedAt = live.RemovedAt, RemovedByParticipantId = live.RemovedByParticipantId, EditedAt = live.EditedAt,
            Permissions = live.Permissions, OperationId = live.OperationId, Facets = live.Facets,
            OfMessageId = live.Id, PreviousChangeId = live.ChangeId,
        };
        // The vendored framework copy exposes no partitioned insert (Entity.Insert arrives upstream
        // after its pin), so the write-once construction rests on the fresh GUIDv7 identity — this
        // method is the only writer into the changelog partition — plus an absence guard under the
        // upsert verb. Swap to Post.Insert(snapshot, ChangelogPartition, token) when the pin moves.
        if (await Post.Get(snapshot.Id, Post.ChangelogPartition, token) is not null)
            throw new InvalidDataException("The changelog snapshot identity already exists.");
        await snapshot.Save(Post.ChangelogPartition, token);
        live.ChangeId = snapshot.Id;
    }

}
