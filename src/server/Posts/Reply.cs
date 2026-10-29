using Koan.Data.Core;
using Tangent.Access;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Replies in a Topic: a Post carrying the root's Tangent and Topic and the Topic's next sequence, the root's
/// counters and last activity moved in the same commit, and <see cref="EventKind.PostCreated"/> appended with them,
/// its target the parent's author unless the author replies to their own Post, so a reply reaches whom it answers
/// through the log's target index, and one <see cref="EventKind.Mention"/> per participant the body mentions
/// (<see cref="Mentions.Notify"/>). The parent is the root unless a Post of this Topic is named. The Topic exists for
/// the author, who holds <see cref="Bit.View"/> in its Tangent, or the command answers that no Topic has the Id; the
/// author holds <see cref="Bit.Post"/> on the Topic, which no one does on a locked or removed one, or is refused with
/// the gate word. Idempotent by Id: the same Id with the same request answers with the existing Post, before any
/// gate, so a replay is safe; a Post of this Topic holding the Id with a different parent, body or author is a conflict,
/// and a Post anywhere else holding it is refused with <see cref="Post.IdTaken"/>, which says nothing of that
/// Post.</summary>
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

        var existing = await Post.Get(Id, ct);
        if (existing is not null && Same(existing)) return new Outcome<Post>.Done(existing);
        if (capabilities.Gate(Bit.Post) is { } gate) return new Outcome<Post>.Refused(gate);
        if (existing is not null)
        {
            return existing.TopicId == root.Id
                ? new Outcome<Post>.Conflict($"Post '{Id}' already exists with a different body.")
                : new Outcome<Post>.Refused(Post.IdTaken);
        }
        var parent = ParentId is null || ParentId == root.Id ? root : await Post.Get(ParentId, ct);
        if (parent?.TopicId != root.Id) return new Outcome<Post>.Refused(NoParent);

        var at = At ?? DateTimeOffset.UtcNow;
        var reply = root.Reply(Id, Body, AuthorId, parent.Id, at);
        await reply.Save(ct);
        await root.Save(ct);
        await Event.Next(sequences, EventKind.PostCreated, at,
            tangentId: reply.TangentId, topicId: reply.TopicId, postId: reply.Id, actorId: reply.AuthorId,
            targetId: parent.AuthorId == reply.AuthorId ? null : parent.AuthorId, version: reply.Version).Save(ct);
        await Mentions.Notify(sequences, reply, reply.AuthorId, null, at, ct);
        return new Outcome<Post>.Done(reply);
    }

    /// <summary>Whether the existing row is this request: the same Topic, parent, body and author.</summary>
    private bool Same(Post existing) =>
        existing.TopicId == TopicId && existing.ParentId == (ParentId ?? TopicId)
        && existing.Body == Body && existing.AuthorId == AuthorId;
}
