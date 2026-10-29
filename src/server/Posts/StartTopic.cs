using Koan.Data.Abstractions;
using Koan.Data.Core;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Starts a Topic: a root Post in the named Tangent, with a slug derived from the title and made unique in that
/// Tangent by a suffix that never changes, and <see cref="EventKind.PostCreated"/> appended in the same commit.
/// Idempotent by Id: the same Id with the same request answers with the existing Post; a different Tangent, title,
/// kind, body or author is a conflict. The Tangent and the author are taken as given.</summary>
public sealed record StartTopic(
    string Id,
    string TangentId,
    string Title,
    string Body,
    string AuthorId,
    PostKind Kind = PostKind.Topic,
    DateTimeOffset? At = null) : ICommand<Post>
{
    /// <summary>How many numbered suffixes are tried before the Post's own Id becomes the suffix.</summary>
    public const int SuffixBound = 50;

    public async Task<Outcome<Post>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Post.Get(Id, ct) is { } existing)
        {
            return Same(existing)
                ? new Outcome<Post>.Done(existing)
                : new Outcome<Post>.Conflict($"Post '{Id}' already exists with a different body.");
        }

        var at = At ?? DateTimeOffset.UtcNow;
        Post.CheckTitle(Title);
        var root = Post.Root(Id, TangentId, Title, await UniqueSlug(ct), Kind, Body, AuthorId, at);
        await root.Save(ct);
        await Event.Next(sequences, EventKind.PostCreated, at,
            tangentId: root.TangentId, topicId: root.TopicId, postId: root.Id, actorId: root.AuthorId, version: root.Version).Save(ct);
        return new Outcome<Post>.Done(root);
    }

    /// <summary>Whether the existing row is this request: the same Tangent, title, kind, body and author.</summary>
    private bool Same(Post existing) =>
        existing.ParentId is null && existing.TangentId == TangentId && existing.Title == Title.Trim()
        && existing.Kind == Kind && existing.Body == Body && existing.AuthorId == AuthorId;

    /// <summary>The title's slug, or the first numbered suffix of it that no Topic in the Tangent holds; each try is one
    /// counted query. Past the bound, the Post's own Id is the suffix, and the whole still stays within the limit.</summary>
    private async Task<string> UniqueSlug(CancellationToken ct)
    {
        var slug = Slug.From(Title);
        for (var suffix = 1; suffix <= SuffixBound; suffix++)
        {
            var candidate = suffix == 1 ? slug : Slug.WithSuffix(slug, suffix);
            if (!await Taken(candidate, ct)) return candidate;
        }

        return Slug.WithSuffix(slug, Id);
    }

    private async Task<bool> Taken(string slug, CancellationToken ct)
    {
        var tangentId = TangentId;
        return await Post.Count.Where(post => post.TangentId == tangentId && post.Slug == slug, CountStrategy.Exact, ct) > 0;
    }
}
