using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Events;

namespace Tangent.Posts;

/// <summary>Undoes a Post's removal: its words, mentions, images and cover come back from its snapshot as they stood, at the
/// same version (<see cref="Post.Back"/>), so a removed opening's Topic takes replies again; its images are used again as
/// whoever wrote those words may see them (<see cref="MediaUse.Record"/>); and <see cref="EventKind.RemovalUndone"/> is
/// appended, targeting no one, so nobody is told and no mention is sent again, while the removal's line leaves its author's
/// For you. The Post exists for the actor, who holds <see cref="Bit.View"/> in its Tangent, or the command answers that no
/// Post has the Id; a Post that stands answers as it is and writes nothing, so a repeat is safe; who may undo it is
/// <see cref="Gate"/>'s to say. No time limit applies.</summary>
public sealed record UndoRemoval(string PostId, string ActorId, DateTimeOffset? At = null) : ICommand<Post>
{
    /// <summary>The answer where the removed words were not kept, which every removal keeps (<see cref="RemovePost"/>).</summary>
    public static readonly Reason NotKept = Reason.Invalid("Its words weren't kept, so it can't come back.");

    public async Task<Outcome<Post>> Run(CancellationToken ct)
    {
        if (await Post.Get(PostId, ct) is not { } post) return new Outcome<Post>.Refused(RemovePost.NoPost);
        var rows = await Capabilities.LoadRows(ActorId, post.TangentId, ct);
        var tombstone = rows.For(post);
        if (!tombstone.Exists) return new Outcome<Post>.Refused(RemovePost.NoPost);
        if (!post.Removed) return new Outcome<Post>.Done(post);
        if (Gate(post, ActorId, rows.For(null), tombstone) is { } gate) return new Outcome<Post>.Refused(gate);
        if (await AuditPartition.At<Post>(post.Id, post.Version, ct) is not { } kept) return new Outcome<Post>.Refused(NotKept);

        post.Back(kept);
        await post.Save(ct);
        await MediaUse.Record(post, kept.EditedBy ?? kept.AuthorId, [], ct);
        await Event.Append(EventKind.RemovalUndone, At ?? DateTimeOffset.UtcNow, ct,
            tangentId: post.TangentId, topicId: post.TopicId, postId: post.Id, actorId: ActorId, version: post.Version);
        return new Outcome<Post>.Done(post);
    }

    /// <summary>Why <paramref name="actorId"/> may not undo the removal of <paramref name="post"/>, null where they may: a
    /// removal by its author only they undo, as its tombstone lets them remove it (<paramref name="tombstone"/>'s
    /// <see cref="Bit.RemoveOwn"/>), so no one brings back words their author withdrew, and anyone else is told it is removed;
    /// a removal by someone else only whoever holds <see cref="Bit.Moderate"/> in its Tangent (<paramref name="around"/>)
    /// undoes, and its author is told it is removed.</summary>
    public static Reason? Gate(Post post, string? actorId, Capabilities around, Capabilities tombstone)
    {
        if (post.RemovedBy == post.AuthorId) return actorId == post.AuthorId ? tombstone.Gate(Bit.RemoveOwn) : Capabilities.Removed;
        if (around.Gate(Bit.Moderate) is not { } gate) return null;
        return actorId == post.AuthorId ? Capabilities.Removed : gate;
    }
}
