namespace Tangent.Render;

/// <summary>One Topic as a list shows it: its root's title, slug, kind and author, the opening words of its body, the minutes
/// it takes to read, its reply count, when it started and when it last gained a Post, its <see cref="Latest"/> reply and its
/// most recent <see cref="People"/>, and, for a signed-in reader, whether it is <see cref="New"/> to them
/// (<see cref="Events.Unread.InTopic"/>) with <see cref="NewPosts"/>, how many of its Posts are, absent where they never read
/// it and so it is new with no count. <see cref="Edited"/> says who other than its author last edited its root, where someone
/// did. <see cref="Cover"/> is the address of an article's cover, while its root stands and has one. <see cref="In"/> is the
/// Tangent it is in, which an article that leads a list without a cover wears in its place. <see cref="Removed"/> says who
/// removed its root and when, where someone did: the Topic keeps its title, author and replies, and takes no more.</summary>
public sealed record TopicEntry(
    string Id,
    string Slug,
    string Title,
    string Kind,
    string Address,
    Person Author,
    string Opening,
    int ReadMinutes,
    int ReplyCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    TopicEntry.Reply? Latest,
    IReadOnlyList<Person> People,
    bool New,
    long? NewPosts,
    Tangents.TangentPage.Edited? Edited = null,
    string? Cover = null,
    Posts.TopicPage.Removal? Removed = null) : IPart
{
    /// <summary>The Tangent the Topic is in, its name and tile, where a list draws it as an article.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public TangentEntry? In { get; init; }

    /// <summary>A Topic's latest reply: its author, its first words, when it was written, whether it is new to the reader,
    /// and who other than its author last edited it, where someone did.</summary>
    public sealed record Reply(string Id, Person Author, string Line, DateTimeOffset At, bool New, Tangents.TangentPage.Edited? Edited = null);
}
