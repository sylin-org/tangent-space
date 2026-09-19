using Koan.Data.Core;
using Tangent.Activity;
using Tangent.Access;
using Tangent.Community;
using Tangent.Stewardship;

namespace Tangent.Conversation;

public sealed partial class ConversationService
{
    /// <summary>An author's edit or removal, or a moderator's removal, applied in one policy pass.
    /// Idempotency lives in the operation receipt registry at the adapter, not here: the same
    /// request replays its recorded result there, and a divergent one is a conflict before this
    /// method runs. The pre-edit snapshot rides the changelog partition either way.</summary>
    public async Task<PostChangeResult> ChangePost(string participantId, string roomKey, string messageId, string? text,
        bool delete, CancellationToken ct, IReadOnlyList<PostFacet>? facets = null)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("A post ID is required.");
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
            var result = await governance.WithCurrentPolicy(participantId, roomKey, async (policy, token) =>
            {
                var current = await ReadPostForChange(policy, roomKey, messageId, token);
                RequirePostChange(policy, current, delete);
                string state, detail;
                if (delete && current.AuthorParticipantId != participantId)
                {
                    await SnapshotChange(current, token);
                    current.Removed = true; current.Content = new PostContent("", current.Content.CreatedAt, current.Content.ReplyTo);
                    current.RemovedAt = clock.GetUtcNow(); current.RemovedByParticipantId = participantId;
                    // The words are gone; their byte ranges are meaningless on a removed row.
                    current.Facets = null;
                    state = "moderated"; detail = "post-removed-by-moderator";
                }
                else if (delete)
                {
                    await SnapshotChange(current, token);
                    current.Removed = true;
                    current.Content = new PostContent("", current.Content.CreatedAt, current.Content.ReplyTo);
                    current.RemovedAt = clock.GetUtcNow();
                    current.RemovedByParticipantId = participantId;
                    current.Facets = null;
                    state = "deleted"; detail = "post-deleted";
                }
                else
                {
                    // D2a: provided facets ride the new version; absent facets re-detect
                    // deterministically. The pre-edit structure rides its snapshot.
                    await SnapshotChange(current, token);
                    current.Content = new PostContent(text!, current.Content.CreatedAt, current.Content.ReplyTo);
                    current.EditedAt = clock.GetUtcNow();
                    current.Facets = facets;
                    state = "accepted"; detail = "post-edited";
                }
                await current.Save(token);
                var topic = await Topic.Get(roomKey, token);
                await ActivityJournal.AppendInTransaction(delete ? ActivityKind.PostDeleted : ActivityKind.PostEdited,
                    roomKey, participantId, current.AuthorParticipantId, topic?.TangentKey, current.Sequence, clock.GetUtcNow(), token);
                return new PostChangeResult(state, messageId, detail);
            }, ct);
            signals.Pulse(roomKey);
            signals.Pulse();
            return result;
        }
        finally { writes.Release(); }
    }

    /// <summary>Mint the pre-edit snapshot into the changelog partition and advance the live row's
    /// ChangeId pointer. Snapshots are write-once: this method is the only writer into the partition,
    /// every snapshot carries a fresh GUIDv7 identity, and an absence guard refuses any replay of a
    /// used identity. Must run inside the caller's governance transaction so the snapshot, the live
    /// mutation and the activity journal commit together or not at all — a failure between the snapshot
    /// write and the live save rolls the transaction back.</summary>
    private static async Task SnapshotChange(Post live, CancellationToken token)
    {
        var snapshot = new Post
        {
            Id = Guid.CreateVersion7().ToString(),
            TopicKey = live.TopicKey, AuthorParticipantId = live.AuthorParticipantId,
            Sequence = live.Sequence, AcceptedAt = live.AcceptedAt, Content = live.Content, Removed = live.Removed,
            RemovedAt = live.RemovedAt, RemovedByParticipantId = live.RemovedByParticipantId, EditedAt = live.EditedAt,
            Permissions = live.Permissions, OperationId = live.OperationId, Facets = live.Facets,
            OfPostId = live.Id, PreviousChangeId = live.ChangeId,
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
