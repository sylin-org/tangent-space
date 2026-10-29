using Koan.Data.Abstractions;
using Koan.Data.Core;
using Tangent.Access;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Starts a Topic: a root Post in the named Tangent, with a slug derived from the title and made unique in that
/// Tangent by a suffix that never changes — a slug a Tangent's own pages hold (<see cref="Slug.Reserved"/>) takes the
/// suffix too — the Tangent's Topic count raised and <see cref="EventKind.PostCreated"/> appended in the same commit.
/// The Tangent exists for the author, who holds <see cref="Bit.View"/> in it, or the command answers that no Tangent
/// has the slug; the author holds <see cref="Bit.Topic"/> there, or is refused with the gate word. A command that
/// names no kind starts a Topic of the Tangent's <see cref="Tangents.Tangent.DefaultKind"/>, so a Topic started in an
/// articles Tangent is an article. Idempotent by Id: the same Id with the same request answers with the existing Post;
/// a different Tangent, title, kind, body or author is a conflict.</summary>
public sealed record StartTopic(
    string Id,
    string TangentId,
    string Title,
    string Body,
    string AuthorId,
    PostKind? Kind = null,
    DateTimeOffset? At = null) : ICommand<Post>
{
    /// <summary>How many numbered suffixes are tried before the Post's own Id becomes the suffix.</summary>
    public const int SuffixBound = 50;

    public const string NoTangent = "No Tangent has this slug.";

    public async Task<Outcome<Post>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Tangents.Tangent.Get(TangentId, ct) is not { } tangent) return new Outcome<Post>.Refused(NoTangent);
        var capabilities = await Capabilities.Load(AuthorId, TangentId, null, ct);
        if (!capabilities.Exists) return new Outcome<Post>.Refused(NoTangent);

        var kind = Kind ?? tangent.DefaultKind;
        if (await Post.Get(Id, ct) is { } existing)
        {
            return Same(existing, kind)
                ? new Outcome<Post>.Done(existing)
                : new Outcome<Post>.Conflict($"Post '{Id}' already exists with a different body.");
        }

        if (capabilities.Gate(Bit.Topic) is { } gate) return new Outcome<Post>.Refused(gate);

        var at = At ?? DateTimeOffset.UtcNow;
        Post.CheckTitle(Title);
        var root = Post.Root(Id, TangentId, Title, await UniqueSlug(ct), kind, Body, AuthorId, at);
        await root.Save(ct);
        tangent.TopicStarted();
        await tangent.Save(ct);
        await Event.Next(sequences, EventKind.PostCreated, at,
            tangentId: root.TangentId, topicId: root.TopicId, postId: root.Id, actorId: root.AuthorId, version: root.Version).Save(ct);
        return new Outcome<Post>.Done(root);
    }

    /// <summary>Whether the existing row is this request: the same Tangent, title, kind, body and author.</summary>
    private bool Same(Post existing, PostKind kind) =>
        existing.ParentId is null && existing.TangentId == TangentId && existing.Title == Title.Trim()
        && existing.Kind == kind && existing.Body == Body && existing.AuthorId == AuthorId;

    /// <summary>The title's slug, or the first numbered suffix of it that no Topic in the Tangent holds; each try is one
    /// counted query. Past the bound, the Post's own Id is the suffix, and the whole still stays within the limit.</summary>
    private async Task<string> UniqueSlug(CancellationToken ct)
    {
        var slug = Slug.From(Title);
        for (var suffix = 1; suffix <= SuffixBound; suffix++)
        {
            if (suffix == 1 && Slug.Reserved.Contains(slug)) continue;
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
