using Koan.Data.Core;
using Tangent.Access;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Replies in a Topic: a Post carrying the root's Tangent and Topic and the Topic's next sequence, the root's
/// counters and last activity moved in the same commit, and <see cref="EventKind.PostCreated"/> appended with them.
/// The parent is the root unless a Post of this Topic is named. The Topic exists for the author, who holds
/// <see cref="Bit.View"/> in its Tangent, or the command answers that no Topic has the Id; the author holds
/// <see cref="Bit.Post"/> on the Topic, which no one does on a locked or removed one, or is refused with the gate
/// word. Idempotent by Id: the same Id with the same request answers with the existing Post; a different Topic,
/// parent, body or author is a conflict.</summary>
public sealed record Reply(
    string Id,
    string TopicId,
    string Body,
    string AuthorId,
    string? ParentId = null,
    DateTimeOffset? At = null) : ICommand<Post>
{
    public const string NoTopic = "No Topic has this Id.";
    public const string NoParent = "The parent is not a Post of this Topic.";

    public async Task<Outcome<Post>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Post.Get(TopicId, ct) is not { } root) return new Outcome<Post>.Refused(NoTopic);
        var capabilities = await Capabilities.Load(AuthorId, root.TangentId, root, ct);
        if (!capabilities.Exists) return new Outcome<Post>.Refused(NoTopic);

        if (await Post.Get(Id, ct) is { } existing)
        {
            return Same(existing)
                ? new Outcome<Post>.Done(existing)
                : new Outcome<Post>.Conflict($"Post '{Id}' already exists with a different body.");
        }

        if (capabilities.Gate(Bit.Post) is { } gate) return new Outcome<Post>.Refused(gate);
        var parentId = ParentId ?? root.Id;
        if (parentId != root.Id && (await Post.Get(parentId, ct))?.TopicId != root.Id) return new Outcome<Post>.Refused(NoParent);

        var at = At ?? DateTimeOffset.UtcNow;
        var reply = root.Reply(Id, Body, AuthorId, parentId, at);
        await reply.Save(ct);
        await root.Save(ct);
        await Event.Next(sequences, EventKind.PostCreated, at,
            tangentId: reply.TangentId, topicId: reply.TopicId, postId: reply.Id, actorId: reply.AuthorId, version: reply.Version).Save(ct);
        return new Outcome<Post>.Done(reply);
    }

    /// <summary>Whether the existing row is this request: the same Topic, parent, body and author.</summary>
    private bool Same(Post existing) =>
        existing.TopicId == TopicId && existing.ParentId == (ParentId ?? TopicId)
        && existing.Body == Body && existing.AuthorId == AuthorId;
}
