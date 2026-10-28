using Koan.Data.Core;
using Tangent.Activity;
using Tangent.Access;
using Tangent.Community;
using Tangent.Stewardship;

namespace Tangent.Conversation;

public sealed partial class ConversationService
{
    /// <summary>An author's edit or removal, or a moderator's removal, applied under current policy.
    /// These writes have no general replay guarantee. A client that loses the response must read
    /// current state before deciding whether another mutation is appropriate.</summary>
    public async Task<PostChangeResult> ChangePost(string participantId, string topicKey, string messageId, string? text,
        bool delete, CancellationToken ct, IReadOnlyList<PostFacet>? facets = null, string? format = null)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("A post ID is required.");
        if (!delete)
        {
            if (text is null) throw new ArgumentException("Text is required for an edit.");
            PostContent.CheckText(text);
            PostFacets.Check(text, facets);
            if (format is not null) PostContent.CheckFormat(format);
        }
        else if (facets is { Count: > 0 }) throw new ArgumentException("A removal cannot carry facets.");
        await writes.WaitAsync(ct);
        try
        {
            var result = await governance.WithinAcceptanceAsync(participantId, topicKey, async (policy, token) =>
            {
                var current = await ReadPostForChange(policy, topicKey, messageId, token);
                RequirePostChange(policy, current, delete);
                var nextFormat = PostContent.CheckFormat(format ?? current.Content.Format);
                if (!delete && nextFormat == PostContent.Markdown)
                {
                    if (facets is { Count: > 0 }) throw new ArgumentException("Markdown posts cannot carry text facets.");
                }
                if (!delete) WindowPlanner.CheckContent(text!, nextFormat);
                if (!delete) await media.ValidateReferences(participantId, topicKey, current.Id, text!, nextFormat, token);
                string state, detail;
                if (delete && current.AuthorParticipantId != participantId)
                {
                    await SnapshotChange(current, token);
                    current.Removed = true; current.Content = current.Content with { Text = "" };
                    current.RemovedAt = clock.GetUtcNow(); current.RemovedByParticipantId = participantId;
                    // The words are gone; their byte ranges are meaningless on a removed row.
                    current.Facets = null;
                    state = "moderated"; detail = "post-removed-by-moderator";
                }
                else if (delete)
                {
                    await SnapshotChange(current, token);
                    current.Removed = true;
                    current.Content = current.Content with { Text = "" };
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
                    current.Content = current.Content with { Text = text!, Format = nextFormat };
                    current.EditedAt = clock.GetUtcNow();
                    current.Facets = nextFormat == PostContent.Markdown ? Array.Empty<PostFacet>() : facets;
                    state = "accepted"; detail = "post-edited";
                }
                await current.Save(token);
                if (!delete) await media.BindReferences(participantId, topicKey, current.Id, text!, nextFormat, token);
                var topic = await Topic.Get(topicKey, token);
                await ActivityJournal.AppendInTransaction(delete ? ActivityKind.PostDeleted : ActivityKind.PostEdited,
                    topicKey, participantId, current.AuthorParticipantId, topic?.TangentKey, current.Sequence, clock.GetUtcNow(), token);
                return new PostChangeResult(state, messageId, detail);
            }, ct);
            signals.Pulse(topicKey);
            signals.Pulse();
            return result;
        }
        finally { writes.Release(); }
    }

    /// <summary>Mint the pre-edit snapshot into the changelog partition and advance the live row's
    /// ChangeId pointer. Snapshots are write-once: this method is the only writer into the partition,
    /// every snapshot carries a fresh GUIDv7 identity, and an absence guard refuses a used identity.
    /// Koan's deferred write sequence may fail partway through, so this snapshot is audit evidence
    /// when present rather than an atomic rollback promise.</summary>
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
