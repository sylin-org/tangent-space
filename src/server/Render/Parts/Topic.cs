namespace Tangent.Render;

/// <summary>A Topic's rail: the way back to its Tangent and where a new Topic is started there, the replies its window holds,
/// the one the address named marked, and an article's outline.</summary>
public sealed record TopicRail(Posts.TopicPage.InTangent Tangent, bool Article, string? NewTopic, IReadOnlyList<Posts.TopicPage.Post> Replies,
    IReadOnlyList<Posts.Outline.Entry> Outline) : IPart;

/// <summary>An article as its Topic opens: its head, where it was taken on a tangent from, its body, where it stands on its
/// author's own account, and what the network says about it.</summary>
public sealed record Piece(ArticleHead Head, Posts.TopicPage.Origin? From, Posts.TopicPage.Post Root, Connections.Publishing? Publishing,
    Connections.Elsewhere.Line? Elsewhere) : IPart;

/// <summary>A discussion's head: its title, when it started, how many replies it has, and whether its moderators keep it
/// at the top of its Tangent and whether they closed it.</summary>
public sealed record TopicTitle(string Title, DateTimeOffset Started, int Replies, bool Closed = false, bool Pinned = false) : IPart;

/// <summary>How an article opens: its cover, the proprietor's article header where they wrote one, its title, who wrote it,
/// as the account they wrote it as, when, the minutes it takes to read, and its outline, folded where the rail folds.</summary>
public sealed record ArticleHead(string Title, string? Cover, Person Author, DateTimeOffset At, int Minutes, string? Header,
    IReadOnlyList<Posts.Outline.Entry> Outline) : IPart
{
    /// <summary>The cover's field, while the article is written.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public Microsoft.AspNetCore.Components.RenderFragment? CoverSlot { get; init; }

    /// <summary>The title's field, while the article is written.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public Microsoft.AspNetCore.Components.RenderFragment? TitleSlot { get; init; }
}

/// <summary>One window of a Topic's replies in order, with which replies it holds by their place in the Topic, the way to
/// the older and the newer window, the form that says it is seen, the reading island's props for a signed-in reader, and the
/// live updater.</summary>
public sealed record TopicReplies(IReadOnlyList<Posts.TopicPage.Post> Posts, int ReplyCount, long? From, long? Through, string? Older, string? Newer,
    Form? Seen, string? Reading, LiveUpdates Live) : IPart;

/// <summary>Where a reader replies under a Topic's replies: the reply form for whoever may reply, with whose words these are,
/// the Post it answers where the address named someone else's reply, the Topic's address and where its pictures and
/// suggestions are; otherwise why they may not, and where a visitor signs in. Where its moderators closed the Topic, who
/// closed it and when, above the form or in place of it.</summary>
public sealed record ReplyBox(Form? Reply, string? Participant, string? Handle, Posts.TopicPage.Post? Parent, string TopicId, string Address,
    string Door, string Bring, string Suggest, string? Gate, string? SignIn, Posts.TopicPage.Closure? Closed = null) : IPart;
