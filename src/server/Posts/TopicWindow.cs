using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Core.Sorting;

namespace Tangent.Posts;

/// <summary>One bounded window of a Topic's replies, in <see cref="Post.Sequence"/> order, read through the window index
/// <c>ix_post_windows</c> (<c>TopicId</c>, <c>Sequence</c>): each read is one range of it with a limit, and nothing is
/// counted. A window opens after a sequence, before one, or around a Post, and holds at most
/// <see cref="WindowPlanner.Limit"/> replies within the byte budget <see cref="WindowPlanner"/> plans. Its cursors are
/// sequence numbers: the replies it holds run from <see cref="Window.Low"/> to <see cref="Window.High"/>, the older
/// window is the one before <see cref="Window.Low"/> and the newer the one after <see cref="Window.High"/>, so following
/// them covers every reply once. Every read stops at the Topic's last sequence as its root was read, the window's
/// boundary, so a reply written meanwhile waits for the next read. A Topic's sequences have no gap — the writer keeps a
/// sequence only when its command commits, and a removed Post stays as a tombstone — so whether an older or a newer
/// window exists follows from the edges without a read. Reads only and takes no lock; whom the window may be shown to is
/// the caller's to decide.</summary>
public static class TopicWindow
{
    /// <summary>The replies a window holds and the sequences it covers: <see cref="Low"/> through <see cref="High"/>,
    /// where an empty window has <see cref="High"/> one below <see cref="Low"/>. <see cref="Boundary"/> is the Topic's
    /// last sequence the window was read against.</summary>
    public sealed record Window(IReadOnlyList<Post> Replies, long Low, long High, long Boundary)
    {
        /// <summary>The cursor of the older window: the sequence it ends before; null when this window starts at the
        /// first reply.</summary>
        public long? Older => Low > 1 ? Low : null;

        /// <summary>The cursor of the newer window: the sequence it starts after; null when this window reaches the
        /// boundary.</summary>
        public long? Newer => High < Boundary ? High : null;
    }

    /// <summary>The root of the Topic with this slug in this Tangent, through <c>ix_post_slugs</c>; null when none.</summary>
    public static async Task<Post?> Topic(string tangentId, string slug, CancellationToken ct)
    {
        var rows = await Post.Query(post => post.TangentId == tangentId && post.Slug == slug,
            new QueryDefinition { Page = 1, PageSize = 1 }, ct);
        return rows.Count == 1 && rows[0].ParentId is null ? rows[0] : null;
    }

    /// <summary>The window of the replies after <paramref name="after"/>, oldest first.</summary>
    public static async Task<Window> After(Post root, long after, CancellationToken ct)
    {
        var boundary = root.LastSequence;
        var from = Math.Clamp(after, 0, boundary);
        var rows = await Newer(root.Id, from, boundary, WindowPlanner.Limit, ct);
        var replies = WindowPlanner.Select(rows, 0, WindowPlanner.Limit, Budget(root), Weight);
        return Covering(replies, from + 1, boundary);
    }

    /// <summary>The window of the replies before <paramref name="before"/>, the newest of them last.</summary>
    public static async Task<Window> Before(Post root, long before, CancellationToken ct)
    {
        var boundary = root.LastSequence;
        var until = Math.Clamp(before, 1, boundary + 1);
        var rows = await Older(root.Id, until, WindowPlanner.Limit, ct);
        var replies = WindowPlanner.Select(rows, rows.Count - 1, WindowPlanner.Limit, Budget(root), Weight);
        return Covering(replies, until, boundary);
    }

    /// <summary>The newest window: the replies before the one after the boundary.</summary>
    public static Task<Window> Latest(Post root, CancellationToken ct) => Before(root, root.LastSequence + 1, ct);

    /// <summary>The window around <paramref name="anchor"/>, a reply of this Topic, which it always holds; the replies
    /// before and after it are read as two ranges and the planner balances them.</summary>
    public static async Task<Window> Around(Post root, Post anchor, CancellationToken ct)
    {
        var boundary = Math.Max(root.LastSequence, anchor.Sequence);
        var older = await Older(root.Id, anchor.Sequence, WindowPlanner.Limit - 1, ct);
        var newer = await Newer(root.Id, anchor.Sequence, boundary, WindowPlanner.Limit - 1, ct);
        var rows = older.Append(anchor).Concat(newer).ToList();
        var replies = WindowPlanner.Select(rows, older.Count, WindowPlanner.Limit, Budget(root), Weight);
        return Covering(replies, anchor.Sequence, boundary);
    }

    /// <summary>What a Post weighs in a window: its fields as JSON carries them, a removed Post without its body, and
    /// what the participants its body mentions may weigh.</summary>
    public static int Weight(Post post) => (post.Removed ? 0 : Mentions.In(post.Body).Count * Mentions.Weight) + JsonSerializer.SerializeToUtf8Bytes(new
    {
        post.Id,
        post.ParentId,
        post.Title,
        Body = post.Removed ? null : post.Body,
        post.Version,
        post.Sequence,
        post.AuthorId,
        post.CreatedAt,
        post.RemovedBy,
        post.RemovedAt
    }).Length;

    /// <summary>What the window's replies may weigh: the Topic's budget less its root, which every answer carries.</summary>
    private static int Budget(Post root) => WindowPlanner.Budget(root.Kind) - Weight(root) - WindowPlanner.PostOverhead;

    /// <summary>The window over the replies it holds; an empty one sits at <paramref name="at"/>.</summary>
    private static Window Covering(IReadOnlyList<Post> replies, long at, long boundary) => replies.Count == 0
        ? new Window(replies, at, at - 1, boundary)
        : new Window(replies, replies[0].Sequence, replies[^1].Sequence, boundary);

    /// <summary>The replies after <paramref name="after"/> through <paramref name="boundary"/>, oldest first, at most
    /// <paramref name="take"/>.</summary>
    private static Task<IReadOnlyList<Post>> Newer(string topicId, long after, long boundary, int take, CancellationToken ct)
        => Post.Query(post => post.TopicId == topicId && post.Sequence > after && post.Sequence <= boundary,
            new QueryDefinition { Page = 1, PageSize = take }.WithSort<Post>(sort => sort.OrderBy(post => post.Sequence)), ct);

    /// <summary>The replies before <paramref name="before"/>, oldest first, the newest <paramref name="take"/> of them:
    /// read newest first and turned.</summary>
    private static async Task<IReadOnlyList<Post>> Older(string topicId, long before, int take, CancellationToken ct)
    {
        var rows = await Post.Query(post => post.TopicId == topicId && post.Sequence < before && post.Sequence > 0,
            new QueryDefinition { Page = 1, PageSize = take }.WithSort<Post>(sort => sort.OrderByDescending(post => post.Sequence)), ct);
        return Enumerable.Reverse(rows).ToList();
    }
}
