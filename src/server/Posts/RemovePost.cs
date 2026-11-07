using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Events;

namespace Tangent.Posts;

/// <summary>Removes a Post, leaving a visible tombstone in its place. The Post exists for the remover, who holds
/// <see cref="Bit.View"/> in its Tangent, or the command answers that no Post has the Id; the remover holds
/// <see cref="Bit.RemoveOwn"/> on their own Post or <see cref="Bit.Moderate"/> on any, or is refused with the gate
/// word. In the same commit the Post as it stood, body and all, goes to the audit partition, where an undo reads it back
/// (<see cref="UndoRemoval"/>), the row becomes the
/// tombstone (<see cref="Post.Remove"/>), and <see cref="EventKind.PostRemoved"/> is appended, targeting the author where
/// someone else removed it, so they are told. A removed root makes
/// its Topic's root a tombstone and leaves its replies as they are. Safe to repeat: removing a tombstone answers with
/// it, unchanged — removed by whoever removed it first — and writes nothing, for anyone who may remove the Post; anyone
/// else is refused with the gate Capabilities names on the tombstone.</summary>
public sealed record RemovePost(string PostId, string RemoverId, DateTimeOffset? At = null) : ICommand<Post>
{
    public static readonly Reason NoPost = EditPost.NoPost;

    public async Task<Outcome<Post>> Run(CancellationToken ct)
    {
        if (await Post.Get(PostId, ct) is not { } post) return new Outcome<Post>.Refused(NoPost);
        var capabilities = await Capabilities.Load(RemoverId, post.TangentId, post, ct);
        if (!capabilities.Exists) return new Outcome<Post>.Refused(NoPost);
        // A tombstone's RemoveOwn is judged as the Post stood, so its remover's retry answers the Post.
        if (capabilities.Gate(Bit.RemoveOwn) is { } gate) return new Outcome<Post>.Refused(gate);
        if (post.Removed) return new Outcome<Post>.Done(post);

        var at = At ?? DateTimeOffset.UtcNow;
        await AuditPartition.Snapshot(post, post.Version, ct);
        var used = post.UsedMedia();
        post.Remove(RemoverId, at);
        await post.Save(ct);
        await MediaUse.Record(post, RemoverId, used, ct);
        await Event.Append(EventKind.PostRemoved, at, ct,
            tangentId: post.TangentId, topicId: post.TopicId, postId: post.Id, actorId: RemoverId,
            targetId: RemoverId == post.AuthorId ? null : post.AuthorId, version: post.Version);
        return new Outcome<Post>.Done(post);
    }
}
