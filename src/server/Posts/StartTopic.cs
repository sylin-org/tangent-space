using Koan.Data.Abstractions;
using Koan.Data.Core;
using Tangent.Access;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Starts a Topic: a root Post in the named Tangent, with a slug derived from the title and made unique in that
/// Tangent by a suffix that never changes — a slug a Tangent's own pages hold (<see cref="Slug.Reserved"/>) takes the
/// suffix too — the Tangent's Topic count raised and <see cref="EventKind.PostCreated"/> appended in the same commit, with
/// one <see cref="EventKind.Mention"/> per participant the body mentions (<see cref="Mentions.Notify"/>).
/// The Tangent exists for the author, who holds <see cref="Bit.View"/> in it, or the command answers that no Tangent
/// has the slug; the author holds <see cref="Bit.Topic"/> there, or is refused with the gate word. A command that
/// names no kind starts a Topic of the Tangent's <see cref="Tangents.Tangent.DefaultKind"/>, so a Topic started in an
/// articles Tangent is an article. Idempotent by Id: the same Id with the same request answers with the existing Post,
/// before any gate, so a replay is safe, whoever the handles it typed name by then; a Post of this Tangent holding the
/// Id with a different title, kind, body or author is a conflict, and a Post anywhere else holding it is refused with
/// <see cref="Post.IdTaken"/>, which says nothing of that Post. The body is judged where the command is built (<see cref="PostBody"/>), never in the
/// writer. A Topic taken on a tangent names its <see cref="OriginId"/>: the origin exists for the author, who holds
/// <see cref="Bit.View"/> in its Tangent, or the command answers that no Post has the Id before anything else; a removed
/// origin is refused with <see cref="Capabilities.Removed"/>; the root carries the origin, may start without a body of
/// the author's own (<see cref="Post.RootBody"/>), and its <see cref="EventKind.PostCreated"/> targets the origin's
/// author unless they take their own Post on a tangent, so the author of what a Topic answers hears of it as the author
/// of what a reply answers does, wherever they may view the new Topic's Tangent.</summary>
public sealed record StartTopic(
    string Id,
    string TangentId,
    string Title,
    PostBody Body,
    string AuthorId,
    PostKind? Kind = null,
    DateTimeOffset? At = null,
    string? OriginId = null) : ICommand<Post>
{
    /// <summary>How many numbered suffixes are tried before the Post's own Id becomes the suffix.</summary>
    public const int SuffixBound = 50;

    public const string NoTangent = "No Tangent has this slug.";

    public const string NoOrigin = "No Post has the origin's Id.";

    public async Task<Outcome<Post>> Run(Sequences sequences, CancellationToken ct)
    {
        Post? origin = null;
        if (OriginId is not null && ((origin = await Post.Get(OriginId, ct)) is null || !(await Capabilities.Load(AuthorId, origin.TangentId, origin, ct)).Exists))
            return new Outcome<Post>.Refused(NoOrigin);
        if (await Tangents.Tangent.Get(TangentId, ct) is not { } tangent) return new Outcome<Post>.Refused(NoTangent);
        var capabilities = await Capabilities.Load(AuthorId, TangentId, null, ct);
        if (!capabilities.Exists) return new Outcome<Post>.Refused(NoTangent);

        var kind = Kind ?? tangent.DefaultKind;
        var existing = await Post.Get(Id, ct);
        if (existing is not null && Same(existing, kind)) return new Outcome<Post>.Done(existing);
        if (origin is { Removed: true }) return new Outcome<Post>.Refused(Capabilities.Removed);
        if (capabilities.Gate(Bit.Topic) is { } gate) return new Outcome<Post>.Refused(gate);
        if (existing is not null)
        {
            return existing.TangentId == TangentId
                ? new Outcome<Post>.Conflict($"Post '{Id}' already exists with a different body.")
                : new Outcome<Post>.Refused(Post.IdTaken);
        }

        var at = At ?? DateTimeOffset.UtcNow;
        Post.CheckTitle(Title);
        var root = Post.Root(Id, TangentId, Title, await UniqueSlug(TangentId, Title, Id, ct), kind, Body, AuthorId, at, origin?.Id);
        await root.Save(ct);
        tangent.TopicStarted();
        await tangent.Save(ct);
        await Event.Next(sequences, EventKind.PostCreated, at,
            tangentId: root.TangentId, topicId: root.TopicId, postId: root.Id, actorId: root.AuthorId,
            targetId: origin is null || origin.AuthorId == root.AuthorId ? null : origin.AuthorId, version: root.Version).Save(ct);
        await Mentions.Notify(sequences, root, root.AuthorId, [], at, ct);
        return new Outcome<Post>.Done(root);
    }

    /// <summary>Whether the existing row is this request: the same Tangent, title, kind, author and origin, and the same
    /// words sent, whoever their typed handles name now.</summary>
    private bool Same(Post existing, PostKind kind) =>
        existing.ParentId is null && existing.TangentId == TangentId && existing.Title == Title.Trim()
        && existing.Kind == kind && existing.SentHash == Body.SentHash && existing.AuthorId == AuthorId
        && existing.OriginId == OriginId;

    /// <summary>The slug of <paramref name="title"/> for a Topic whose root is <paramref name="id"/>, or the first numbered
    /// suffix of it that no Topic in <paramref name="tangentId"/> holds; each try is one counted query. Past the bound, the
    /// Post's own Id is the suffix, and the whole still stays within the limit.</summary>
    internal static async Task<string> UniqueSlug(string tangentId, string title, string id, CancellationToken ct)
    {
        var slug = Slug.From(title);
        for (var suffix = 1; suffix <= SuffixBound; suffix++)
        {
            if (suffix == 1 && Slug.Reserved.Contains(slug)) continue;
            var candidate = suffix == 1 ? slug : Slug.WithSuffix(slug, suffix);
            if (!await Taken(tangentId, candidate, ct)) return candidate;
        }

        return Slug.WithSuffix(slug, id);
    }

    private static async Task<bool> Taken(string tangentId, string slug, CancellationToken ct)
        => await Post.Count.Where(post => post.TangentId == tangentId && post.Slug == slug, CountStrategy.Exact, ct) > 0;
}
