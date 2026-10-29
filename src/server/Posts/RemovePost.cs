using Koan.Data.Core;
using Tangent.Access;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Removes a Post, leaving a visible tombstone in its place. The Post exists for the remover, who holds
/// <see cref="Bit.View"/> in its Tangent, or the command answers that no Post has the Id; the remover holds
/// <see cref="Bit.RemoveOwn"/> on their own Post or <see cref="Bit.Moderate"/> on any, or is refused with the gate
/// word. In the same commit the Post as it stood, body and all, goes to the audit partition, the row becomes the
/// tombstone (<see cref="Post.Remove"/>), and <see cref="EventKind.PostRemoved"/> is appended. A removed root makes
/// its Topic's root a tombstone and leaves its replies as they are. Safe to repeat: removing a Post the remover
/// already removed answers with the same tombstone and writes nothing; anyone else is refused with
/// <c>removed</c>.</summary>
public sealed record RemovePost(string PostId, string RemoverId, DateTimeOffset? At = null) : ICommand<Post>
{
    public const string NoPost = EditPost.NoPost;

    public async Task<Outcome<Post>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Post.Get(PostId, ct) is not { } post) return new Outcome<Post>.Refused(NoPost);
        var capabilities = await Capabilities.Load(RemoverId, post.TangentId, post, ct);
        if (!capabilities.Exists) return new Outcome<Post>.Refused(NoPost);
        if (post.Removed && post.RemovedBy == RemoverId) return new Outcome<Post>.Done(post);
        if (!capabilities.Holds(Bit.RemoveOwn) && !capabilities.Holds(Bit.Moderate))
            return new Outcome<Post>.Refused(capabilities.Gate(Bit.RemoveOwn)!);

        var at = At ?? DateTimeOffset.UtcNow;
        await AuditPartition.Snapshot(post, post.Version, ct);
        post.Remove(RemoverId, at);
        await post.Save(ct);
        await Event.Next(sequences, EventKind.PostRemoved, at,
            tangentId: post.TangentId, topicId: post.TopicId, postId: post.Id, actorId: RemoverId, version: post.Version).Save(ct);
        return new Outcome<Post>.Done(post);
    }
}
