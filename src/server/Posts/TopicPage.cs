using System.Security.Cryptography;
using System.Text.Json;
using Tangent.Render;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>A Topic as a resource, the same for a person and an agent: its root — <see cref="Title"/>,
/// <see cref="Kind"/>, author, when it started and its body, in <see cref="Root"/> — and one bounded window of its
/// <see cref="Replies"/> in sequence order. The window holds the sequences <see cref="From"/> through
/// <see cref="Through"/> (both absent when it holds none); <see cref="Older"/> and <see cref="Newer"/> are the addresses
/// of the windows on either side, each absent where nothing lies that way, and their cursors are sequence numbers.
/// <see cref="Anchor"/> is the Post the window was opened around, where its address named one. <see cref="Can"/> is what
/// the reader holds on the Topic — <c>Post</c> among it when they may reply — and <see cref="Gate"/> the one reason they
/// may not, where they may not; <see cref="Reply"/> is the form that replies, present only where they may, answering
/// the Post the page's address named where that is someone else's reply and not removed, and the Topic otherwise. For a
/// signed-in reader, <see cref="Seen"/> is the form that moves their checkpoint on the Topic through what the page
/// shows, present while the window continues from that checkpoint and reaches past it, so the page never offers to skip
/// a reply it did not show, and <see cref="Watching"/> their watch flag on it with the form that sets it. Nothing here is a total: <see cref="ReplyCount"/> is the root's own counter.
/// <see cref="Tangent"/> is where the Topic is; <see cref="Place"/>, <see cref="Door"/> and <see cref="Who"/> say where
/// the page is and who reads it; <see cref="Day"/> is the UTC day its times are written for. <see cref="TakenFrom"/> is
/// where a Topic taken on a tangent was taken from, as the reader may know it.</summary>
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
    Form? Reply,
    Form? Seen,
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
    DateOnly Day,
    TopicPage.Origin? TakenFrom = null)
{
    /// <summary>One Post as the page shows it: its place in the Topic, its address, author, body and when it was
    /// written, its version, and, for a reply to a Post other than the root, that Post's Id and address. The body is
    /// the Markdown as written, and <see cref="Mentions"/> are the participants it mentions as they are now, which the
    /// HTML renders as links reading their current handles. A removed Post
    /// is a tombstone that keeps its place, author and time, carries no body and says who removed it and when.
    /// <see cref="Edited"/> says who other than its author last edited it and when, where someone did: the words are not
    /// said to be the author's.
    /// <see cref="New"/> marks a Post a signed-in reader has not seen — past their checkpoint on the Topic, or every
    /// Post where they have none. <see cref="Can"/> is what the reader holds on this Post, and <see cref="Edit"/> where
    /// it is edited or removed, present only where they may do either. <see cref="TakeOnATangent"/> is where the reader
    /// takes it on a tangent, present only on a Post that is not removed, for a reader who may start a Topic somewhere;
    /// <see cref="Taken"/> holds the Topics taken on a tangent from it that the reader may view, where there are any.
    /// <see cref="Report"/> is the form that reports it to the moderators, with a TID minted for it, for a signed-in
    /// reader, on someone else's Post that is not removed, outside the moderators' Tangent.</summary>
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
        string? Edit,
        string? TakeOnATangent = null,
        TopicPage.Taken? Taken = null,
        Form? Report = null,
        TangentPage.Edited? Edited = null);

    /// <summary>A participant a body mentions: their DID, which the body's link names, their handle now, and their
    /// address.</summary>
    public sealed record Mention(string Id, string Handle, string Address);

    /// <summary>Where a Topic was taken on a tangent from. For a reader who may view the origin: its address — a
    /// tombstone's where it is <see cref="Removed"/> — the title of its Topic, the name of its Tangent and who wrote it.
    /// For anyone else it is <see cref="Absent"/>, and says nothing more.</summary>
    public sealed record Origin(bool Absent, string? Address, string? Topic, string? Tangent, string? Author, bool Removed)
    {
        public static Origin Unseen { get; } = new(true, null, null, null, null, false);
    }

    /// <summary>The Topics taken on a tangent from one Post that the reader may view, newest first, bounded;
    /// <see cref="Count"/> is how many the page found that they may view, and <see cref="AtLeast"/> says more may be
    /// than it found.</summary>
    public sealed record Taken(IReadOnlyList<TakenTopic> Topics, int Count, bool AtLeast);

    /// <summary>One Topic taken on a tangent from a Post: its title, address and Tangent's name, and whether its root is
    /// removed.</summary>
    public sealed record TakenTopic(string Title, string Address, string Tangent, bool Removed);

    /// <summary>Who removed a Post and when.</summary>
    public sealed record Removal(TangentPage.Author By, DateTimeOffset At);

    /// <summary>The Tangent the Topic is in: its slug, name and address.</summary>
    public sealed record InTangent(string Slug, string Name, string Address);

    /// <summary>The signed-in participant reading the page: their id, the name to show, their handle, the highest role
    /// they hold where they hold one, their avatar, where they sign out, coming back to this page, and the place's
    /// settings, for one who holds <c>ManageSpace</c>, and how many of their notifications they have not seen.</summary>
    public sealed record Reader(string Id, string Name, string Handle, string? Role, string? Avatar, string SignOut, string? Settings = null,
        Events.Notifications.Unseen? Notifications = null);

    /// <summary>The version the page's <c>ETag</c> carries: a hash of everything the page says, so the same window for
    /// a visitor on the same day answers the same tag, and a reply, an edit, a removal, a changed <c>can</c> or a new day
    /// changes it. A signed-in reader's page carries their forms with a new antiforgery token every time, so its version
    /// never repeats and it is never answered as unchanged.</summary>
    public static long Version(TopicPage page)
        => BitConverter.ToInt64(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(page)), 0) & long.MaxValue;
}
