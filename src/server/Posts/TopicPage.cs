using System.Security.Cryptography;
using System.Text.Json;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>A Topic as a resource, the same for a person and an agent: its root — <see cref="Title"/>,
/// <see cref="Kind"/>, author, when it started and its body, in <see cref="Root"/> — and one bounded window of its
/// <see cref="Replies"/> in sequence order. The window holds the sequences <see cref="From"/> through
/// <see cref="Through"/> (both absent when it holds none); <see cref="Older"/> and <see cref="Newer"/> are the addresses
/// of the windows on either side, each absent where nothing lies that way, and their cursors are sequence numbers.
/// <see cref="Anchor"/> is the Post the window was opened around, where its address named one. <see cref="Can"/> is what
/// the reader holds on the Topic — <c>Post</c> among it when they may reply — and <see cref="Gate"/> the one reason they
/// may not, where they may not. For a signed-in reader, <see cref="Seen"/> is the form that moves their checkpoint on
/// the Topic through what the page shows, present while the window continues from that checkpoint and reaches past
/// it, so the page never offers to skip a reply it did not show, and <see cref="Watching"/> the form that
/// sets or clears their watch flag on it. Nothing here is a total: <see cref="ReplyCount"/> is the root's own counter.
/// <see cref="Tangent"/> is where the Topic is; <see cref="Place"/>, <see cref="Door"/> and <see cref="Who"/> say where
/// the page is and who reads it; <see cref="Day"/> is the UTC day its times are written for.</summary>
public sealed record TopicPage(
    string Id,
    string Slug,
    string Title,
    string Kind,
    string Address,
    TopicPage.InTangent Tangent,
    bool Locked,
    bool Pinned,
    int ReplyCount,
    IReadOnlyList<string> Can,
    string? Gate,
    Events.TopicSeen? Seen,
    Events.Watching? Watching,
    TopicPage.Post Root,
    IReadOnlyList<TopicPage.Post> Replies,
    long? From,
    long? Through,
    string? Older,
    string? Newer,
    string? Anchor,
    string? NewTopic,
    string Place,
    string Door,
    string? SignIn,
    TopicPage.Reader? Who,
    DateOnly Day)
{
    /// <summary>One Post as the page shows it: its place in the Topic, its address, author, body and when it was
    /// written, its version, and, for a reply to a Post other than the root, that Post's Id and address. The body is
    /// the Markdown as written, and <see cref="Mentions"/> are the participants it mentions as they are now, which the
    /// HTML renders as links reading their current handles. A removed Post
    /// is a tombstone that keeps its place, author and time, carries no body and says who removed it and when.
    /// <see cref="New"/> marks a Post a signed-in reader has not seen — past their checkpoint on the Topic, or every
    /// Post where they have none. <see cref="Can"/> is what the reader holds on this Post, and <see cref="Edit"/> where
    /// it is edited or removed, present only where they may do either.</summary>
    public sealed record Post(
        string Id,
        long Sequence,
        string Address,
        TangentPage.Author Author,
        string? Body,
        IReadOnlyList<Mention> Mentions,
        DateTimeOffset CreatedAt,
        long Version,
        string? ParentId,
        string? Parent,
        Removal? Removed,
        bool New,
        IReadOnlyList<string> Can,
        string? Edit);

    /// <summary>A participant a body mentions: their DID, which the body's link names, their handle now, and their
    /// address.</summary>
    public sealed record Mention(string Id, string Handle, string Address);

    /// <summary>Who removed a Post and when.</summary>
    public sealed record Removal(TangentPage.Author By, DateTimeOffset At);

    /// <summary>The Tangent the Topic is in: its slug, name and address.</summary>
    public sealed record InTangent(string Slug, string Name, string Address);

    /// <summary>The signed-in participant reading the page: their id, the name to show, their handle, the highest role
    /// they hold where they hold one, their avatar, and where they sign out, coming back to this page.</summary>
    public sealed record Reader(string Id, string Name, string Handle, string? Role, string? Avatar, string SignOut);

    /// <summary>The version the page's <c>ETag</c> carries: a hash of everything the page says, so the same window for
    /// a visitor on the same day answers the same tag, and a reply, an edit, a removal, a changed <c>can</c> or a new day
    /// changes it. A signed-in reader's page carries their forms with a new antiforgery token every time, so its version
    /// never repeats and it is never answered as unchanged.</summary>
    public static long Version(TopicPage page)
        => BitConverter.ToInt64(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(page)), 0) & long.MaxValue;
}
