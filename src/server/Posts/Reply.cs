using Koan.Data.Core;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Replies in a Topic: a Post carrying the root's Tangent and Topic and the Topic's next sequence, the root's
/// counters and last activity moved in the same commit, and <see cref="EventKind.PostCreated"/> appended with them.
/// Refused on a locked or removed Topic. Idempotent by Id: the same Id with the same body answers with the existing
/// Post; a different body is a conflict. The author is taken as given.</summary>
public sealed record Reply(
    string Id,
    string TopicId,
    string Body,
    string AuthorId,
    string? ParentId = null,
    DateTimeOffset? At = null) : ICommand<Post>
{
    public const string NoTopic = "No Topic has this Id.";

    public async Task<Outcome<Post>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Post.Get(Id, ct) is { } existing)
        {
            return existing.Body == Body
                ? new Outcome<Post>.Done(existing)
                : new Outcome<Post>.Conflict($"Post '{Id}' already exists with a different body.");
        }

        if (await Post.Get(TopicId, ct) is not { } root) return new Outcome<Post>.Refused(NoTopic);

        var at = At ?? DateTimeOffset.UtcNow;
        var reply = root.Reply(Id, Body, AuthorId, ParentId ?? root.Id, at);
        await reply.Save(ct);
        await root.Save(ct);
        await Event.Next(sequences, EventKind.PostCreated, at,
            tangentId: reply.TangentId, topicId: reply.TopicId, postId: reply.Id, actorId: reply.AuthorId, version: reply.Version).Save(ct);
        return new Outcome<Post>.Done(reply);
    }
}
