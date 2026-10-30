using Koan.Data.Core;
using Tangent.Access;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Removes a Post, leaving a visible tombstone in its place. The Post exists for the remover, who holds
/// <see cref="Bit.View"/> in its Tangent, or the command answers that no Post has the Id; the remover holds
/// <see cref="Bit.RemoveOwn"/> on their own Post or <see cref="Bit.Moderate"/> on any, or is refused with the gate
/// word. In the same commit the Post as it stood, body and all, goes to the audit partition, the row becomes the
/// tombstone (<see cref="Post.Remove"/>), and <see cref="EventKind.PostRemoved"/> is appended, targeting the author where
/// someone else removed it, so they are told. A removed root makes
/// its Topic's root a tombstone and leaves its replies as they are. Safe to repeat: removing a tombstone answers with
/// it, unchanged — removed by whoever removed it first — and writes nothing, for anyone who may remove the Post; anyone
/// else is refused with the gate Capabilities names on the tombstone.</summary>
public sealed record RemovePost(string PostId, string RemoverId, DateTimeOffset? At = null) : ICommand<Post>
{
    public const string NoPost = EditPost.NoPost;

    public async Task<Outcome<Post>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Post.Get(PostId, ct) is not { } post) return new Outcome<Post>.Refused(NoPost);
        var rows = await Capabilities.LoadRows(RemoverId, post.TangentId, ct);
        var capabilities = rows.For(post);
        if (!capabilities.Exists) return new Outcome<Post>.Refused(NoPost);
        // Nothing applies to a tombstone, so whether the remover may remove it is asked of the Post as it stood: its
        // author, holding RemoveOwn, or anyone holding Moderate.
        var asItStood = post.Removed ? rows.For(new Post { Id = post.Id, TangentId = post.TangentId, TopicId = post.TopicId, AuthorId = post.AuthorId }) : capabilities;
        if (!asItStood.Holds(Bit.RemoveOwn) && !asItStood.Holds(Bit.Moderate))
            return new Outcome<Post>.Refused(capabilities.Gate(Bit.RemoveOwn)!);
        if (post.Removed) return new Outcome<Post>.Done(post);

        var at = At ?? DateTimeOffset.UtcNow;
        await AuditPartition.Snapshot(post, post.Version, ct);
        post.Remove(RemoverId, at);
        await post.Save(ct);
        await Event.Next(sequences, EventKind.PostRemoved, at,
            tangentId: post.TangentId, topicId: post.TopicId, postId: post.Id, actorId: RemoverId,
            targetId: RemoverId == post.AuthorId ? null : post.AuthorId, version: post.Version).Save(ct);
        return new Outcome<Post>.Done(post);
    }
}
