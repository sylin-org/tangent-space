using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Space;

namespace Tangent.Capture;

/// <summary>A Topic a seed starts at a time it names, written as the Topics controller writes one past its door
/// (<see cref="PostWriting.Start"/>): the same rows, rules and log, with no door to judge, since a seed writes as whoever it names.
/// Compiled into the capture host for its seeds, and into the proofs, which seed the rows they start from the same way.</summary>
public sealed record Started(string Id, string TangentId, string Title, PostBody Body, string AuthorId, DateTimeOffset? At = null,
    string? OriginId = null, string? CoverId = null) : ICommand<Post>
{
    public async Task<Outcome<Post>> Run(CancellationToken ct)
    {
        var tangent = await Tangents.Tangent.Get(TangentId, ct) ?? throw new Refusal(Capabilities.Absent);
        var origin = OriginId is null ? null : await Post.Get(OriginId, ct) ?? throw new Refusal(Post.NoOrigin);
        var root = new Post { Id = Id, Title = Title };
        await PostWriting.Start(root, tangent, Body, AuthorId, origin, CoverId, At ?? DateTimeOffset.UtcNow, ct);
        await root.Save(ct);
        return new Outcome<Post>.Done(root);
    }
}

/// <summary>A reply a seed makes at a time it names, written as the Posts controller writes one past its door
/// (<see cref="PostWriting.Reply"/>), to <see cref="ParentId"/> or else the Topic.</summary>
public sealed record Replied(string Id, string TopicId, PostBody Body, string AuthorId, string? ParentId = null, DateTimeOffset? At = null)
    : ICommand<Post>
{
    public async Task<Outcome<Post>> Run(CancellationToken ct)
    {
        var root = await Post.Get(TopicId, ct) ?? throw new Refusal(Post.NoTopic);
        var parent = ParentId is null || ParentId == root.Id ? root : await Post.Get(ParentId, ct);
        if (parent?.TopicId != root.Id) throw new Refusal(Post.NoParent);
        var reply = new Post { Id = Id };
        await PostWriting.Reply(reply, root, parent, Body, AuthorId, At ?? DateTimeOffset.UtcNow, ct);
        await reply.Save(ct);
        return new Outcome<Post>.Done(reply);
    }
}

/// <summary>An edit a seed makes at a time it names, from the version it names, written as the Posts controller writes one past its
/// door (<see cref="PostWriting.Edit"/>): one that changes nothing answers with the Post as it stands, and one from another version is
/// refused as stale.</summary>
public sealed record Edited(string PostId, long Version, PostBody Body, string? Title, string EditorId, DateTimeOffset? At = null,
    string? CoverId = null) : ICommand<Post>
{
    public async Task<Outcome<Post>> Run(CancellationToken ct)
    {
        var post = await Post.Get(PostId, ct) ?? throw new Refusal(Post.NoPost);
        if (await PostWriting.Edit(post, post, Version, Body, Title, CoverId, EditorId, At ?? DateTimeOffset.UtcNow, ct)) await post.Save(ct);
        return new Outcome<Post>.Done(post);
    }
}

/// <summary>A Tangent as creating one writes it, with its audience and the row of the log that begins it
/// (<see cref="Tangents.TangentWriting.Opened"/>), on the slug and at the order the seed names, at a time it names.</summary>
public sealed record OpenTangent(string Slug, string Name, string Description, PostKind DefaultKind, int Order, Audience Audience, string ActorId,
    DateTimeOffset? At = null) : ICommand<Tangents.Tangent>
{
    public async Task<Outcome<Tangents.Tangent>> Run(CancellationToken ct)
    {
        var tangent = Tangents.Tangent.Create(Slug, Name, Description, DefaultKind, Order);
        await Tangents.TangentWriting.Opened(tangent, Audience, ActorId, At ?? DateTimeOffset.UtcNow, ct);
        await tangent.Save(ct);
        return new Outcome<Tangents.Tangent>.Done(tangent);
    }
}
