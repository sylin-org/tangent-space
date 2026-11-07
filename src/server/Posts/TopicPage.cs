using System.Security.Cryptography;
using System.Text.Json;
using Koan.Web.OpenGraph;
using Tangent.Render;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>A Topic as a resource, the same for a person and an agent: its root — <see cref="Title"/>,
/// <see cref="Kind"/>, author, when it started and its body, in <see cref="Root"/> — and one bounded window of its
/// <see cref="Replies"/> in sequence order. The window holds the sequences <see cref="From"/> through
/// <see cref="Through"/>; where it holds no reply, <see cref="From"/> is absent and <see cref="Through"/> is the root's
/// zero, which is what the page shows and what its seen form sends; <see cref="Older"/> and <see cref="Newer"/> are the addresses
/// of the windows on either side, each absent where nothing lies that way, and their cursors are sequence numbers.
/// <see cref="Anchor"/> is the Post the window was opened around, where its address named one. <see cref="Can"/> is what
/// the reader holds on the Topic — <c>Post</c> among it when they may reply — and <see cref="Gate"/> the one reason they
/// may not, where they may not; <see cref="Reply"/> is the form that replies, present only where they may, answering
/// the Post the page's address named where that is someone else's reply and not removed, and the Topic otherwise. For a
/// signed-in reader, <see cref="Seen"/> is the form that moves their mark on the Topic through what the page shows,
/// present while the window continues from that mark and reaches past it, so the page never offers to skip a reply it
/// did not show. Nothing here is a total: <see cref="ReplyCount"/> is the root's own counter.
/// <see cref="Tangent"/> is where the Topic is; <see cref="Place"/>, <see cref="Door"/> and <see cref="Who"/> say where
/// the page is and who reads it; <see cref="Day"/> is the UTC day its times are written for. <see cref="TakenFrom"/> is
/// where a Topic taken on a tangent was taken from, as the reader may know it. <see cref="Feeds"/> are its Tangent's
/// feeds, present only where a visitor may read that Tangent. <see cref="ArticleHeader"/> is the place's article header, its Markdown drawn as a
/// Post's body is, present only on an article and only where the place wrote one. <see cref="Cover"/> is the address of an
/// article's cover, while its root stands and has one; <see cref="ReadMinutes"/> what its body takes to read.
/// <see cref="Publishing"/> is where an article stands on its author's own account, for its author
/// (<see cref="Connections.Publishing.For"/>), and <see cref="Document"/> its document record there, once it is out;
/// <see cref="Elsewhere"/> is what the network says about it since, for everyone (<see cref="Connections.Elsewhere.For"/>).
/// <see cref="Closed"/> says who closed the Topic to replies and when, where its moderators did, and <see cref="Pinned"/> that
/// they keep it at the top of its Tangent; <see cref="Closing"/> and <see cref="Pinning"/> are the forms that close or reopen
/// it and pin or unpin it, present only for whoever holds <c>Moderate</c> on it, pinning saying first which Topic it
/// unpins. <see cref="Move"/> is the address of the page that moves it to another Tangent and <see cref="Deleting"/> the form
/// that deletes it whole, carrying how many replies go with it, each present for whoever holds <c>Moderate</c> in its
/// Tangent, a removed root's among them, outside the moderators' Tangent.</summary>
[Render.Page(Render.Layout.Reading)]
public sealed record TopicPage(
    string Id,
    string Slug,
    string Title,
    string Kind,
    string Address,
    TopicPage.InTangent Tangent,
    int ReplyCount,
    IReadOnlyList<string> Can,
    string? Gate,
    Form? Reply,
    Form? Seen,
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
    Render.Who? Who,
    DateOnly Day,
    TopicPage.Origin? TakenFrom = null,
    IReadOnlyList<FeedLink>? Feeds = null,
    string? ArticleHeader = null,
    string? Cover = null,
    int ReadMinutes = 1,
    Connections.Publishing? Publishing = null,
    string? Document = null,
    Connections.Elsewhere.Line? Elsewhere = null,
    TopicPage.Closure? Closed = null,
    bool Pinned = false,
    Form? Closing = null,
    Form? Pinning = null,
    string? Move = null,
    Form? Deleting = null)
{
    [Newtonsoft.Json.JsonIgnore]
    public bool IsArticle => Kind == PostWrites.Word(PostKind.Article);

    /// <summary>An article's outline, from its root's body as the page renders it; none for a discussion or a removed root.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public IReadOnlyList<Outline.Entry> Outlined => IsArticle && Root.Body is { } body
        ? Outline.Of(Mentions.Html(Root.Id, Root.Version, body, Root.Mentions)).Entries
        : [];

    /// <summary>A Post as this page draws it: marked where the address named it, its images under the door, and the author of
    /// the Post it answers where the window holds that Post.</summary>
    private Post Placed(Post post) => post with
    {
        Anchor = Anchor == post.Id,
        Door = Door,
        ParentAuthor = post.ParentId is { } parent ? Replies.FirstOrDefault(reply => reply.Id == parent)?.Author.Name : null
    };

    [Render.Slot(Render.Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public Render.TopicRail Rail => new(Tangent, IsArticle, NewTopic, Replies.Select(Placed).ToList(), Outlined);


    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Piece? Piece => IsArticle
        ? new(new Render.ArticleHead(Title, Cover, Root.Author with { Account = Root.WrittenAs }, Root.CreatedAt, ReadMinutes, ArticleHeader, Outlined),
            TakenFrom, Placed(Root) with
            {
                Root = true,
                Article = true,
                Moderating = Moderating,
                Quote = Reply is null ? null : JsonSerializer.Serialize(new { address = Address })
            }, Publishing, Elsewhere)
        : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.TopicTitle? Heading => IsArticle ? null : new(Title, Root.CreatedAt, ReplyCount, Closed is not null, Pinned);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Origin? Lineage => IsArticle ? null : TakenFrom;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Post? Opening => IsArticle ? null : Placed(Root) with { Root = true, Moderating = Moderating };

    /// <summary>What its root's footer carries for whoever moderates here: closing or reopening, pinning or unpinning, moving
    /// and deleting.</summary>
    private IReadOnlyList<Render.IPart> Moderating => new Render.IPart?[] { Closing, Pinning, Move is { } move ? new Render.LinkButton("Move", move) : null, Deleting }
        .OfType<Render.IPart>().ToList();

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.TopicReplies Window => new(Replies.Select(Placed).ToList(), ReplyCount, From, Through, Older, Newer, Seen,
        Who is { } reader ? JsonSerializer.Serialize(new { participant = reader.Id, topic = Id, title = Title, address = Address, newest = Newer is null }) : null,
        new Render.LiveUpdates(Render.LiveUpdates.TopicPage, Tangent.Slug, Id, Address, Newer is null));

    /// <summary>Whether the reader follows the Topic, so its new replies are for them on their door; absent for a visitor.</summary>
    public bool? Following { get; init; }

    /// <summary>The form that follows the Topic, or stops following it, for a signed-in reader.</summary>
    public Form? Follow { get; init; }

    /// <summary>Following, under the replies, where a phone reads it too.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList? Attention => Follow is { } form
        ? new([new Render.Entry(Following == true ? "Its new replies come to your door." : "Follow it to have its new replies come to your door.", [form])], "Your door")
        : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.ReplyBox Box => new(Reply, Who?.Id, Who?.Handle,
        Reply?.Value(PostWrites.ParentField) is { Length: > 0 } parent ? Replies.FirstOrDefault(reply => reply.Id == parent) : null,
        Id, Address, Door, PostWrites.MediaForm(Tangent.Address).Action, SuggestionsController.Address(Tangent.Address, Slug), Gate, SignIn,
        Root.Removed is null ? Closed : null);

    /// <summary>The frame the shell draws around the page: the place, who reads or where a visitor signs in, its feeds, and
    /// an article's record where it is out.</summary>
    public static Render.Frame Frame(TopicPage page)
        => new(page.Place, page.Door, [], page.Who, page.SignIn, page.Feeds, Document: page.Document);

    /// <summary>What heads a Topic of this <paramref name="kind"/> from the place's <paramref name="appearance"/>, its images under
    /// <paramref name="door"/>.</summary>
    public static string? HeaderOf(PostKind kind, Space.Appearance appearance, string door)
        => kind == PostKind.Article ? Space.PlaceWords.Html(Space.Appearance.ArticleHeaderPart, appearance.ArticleHeader, door) : null;

    /// <summary>One Post as the page shows it: its place in the Topic, its address, author, body and when it was
    /// written, its version, and, for a reply to a Post other than the root, that Post's Id and address. The body is
    /// the Markdown as written, and <see cref="Mentions"/> are the participants it mentions as they are now, which the
    /// HTML renders as links reading their names now. A removed Post
    /// is a tombstone that keeps its place, author and time, carries no body and says who removed it and when.
    /// <see cref="Edited"/> says who other than its author last edited it and when, where someone did: the words are not
    /// said to be the author's.
    /// <see cref="New"/> marks a Post that is new to a signed-in reader (<see cref="Events.Unread.New"/>): someone else's,
    /// past where they were last here and past their mark on the Topic. <see cref="Can"/> is what the reader holds on this Post, <see cref="Edit"/> where
    /// it is edited, present only where they may edit it, and <see cref="Remove"/> the form that removes it, confirmed in the
    /// page, present only where they may remove it and it is not removed, and <see cref="Undo"/> the form that brings a removed one's
    /// words back, present only where they may undo its removal (<see cref="UndoRemoval.Gate"/>). <see cref="TakeOnATangent"/> is the new-topic form that takes
    /// it on a tangent, present only on a Post that is not removed, for a reader who may start a Topic somewhere;
    /// <see cref="Taken"/> holds the Topics taken on a tangent from it that the reader may view, where there are any.
    /// <see cref="Report"/> is the form that reports it to the moderators, with a TID minted for it, for a signed-in
    /// reader, on someone else's Post that is not removed, outside the moderators' Tangent. <see cref="WrittenAs"/> is the
    /// DID of the Bluesky account its author wrote it as, which its author's card links to from it.</summary>
    public sealed record Post(
        string Id,
        long Sequence,
        string Address,
        Render.Person Author,
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
        TangentPage.Edited? Edited = null,
        string? WrittenAs = null,
        Form? Remove = null,
        Form? Undo = null) : Render.IPart
    {
        /// <summary>Whether this is the Topic's root.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool Root { get; init; }

        /// <summary>Whether this is the root of an article, its body alone in the article's type.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool Article { get; init; }

        /// <summary>Whether this is the Post the page's address named.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool Anchor { get; init; }

        /// <summary>The name of the author of the Post this one replies to, where the page holds it.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? ParentAuthor { get; init; }

        /// <summary>The quote island's props, on an article's body where the reader may reply.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? Quote { get; init; }

        /// <summary>The base URL, ending in a slash, under which the body's images have their addresses.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string Door { get; init; } = "";

        /// <summary>What a root's footer carries for whoever moderates its Topic, after its own: closing it, pinning it, moving it
        /// and deleting it.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public IReadOnlyList<Render.IPart> Moderating { get; init; } = [];
    }

    /// <summary>A participant or a role a body mentions, by what the body's link names: a participant's id, their handle
    /// now, the name the place shows them by now, and their address; a role's key, its word, as an <c>@</c> names it, its
    /// name, and no address.</summary>
    public sealed record Mention(string Id, string Handle, string Name, string? Address);

    /// <summary>Where a Topic was taken on a tangent from. For a reader who may view the origin: its address — a
    /// tombstone's where it is <see cref="Removed"/> — the title of its Topic, the name of its Tangent and who wrote it.
    /// For anyone else it is <see cref="Absent"/>, and says nothing more.</summary>
    public sealed record Origin(bool Absent, string? Address, string? Topic, string? Tangent, string? Author, bool Removed) : Render.IPart
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
    public sealed record Removal(Render.Person By, DateTimeOffset At);

    /// <summary>Who closed a Topic to replies and when.</summary>
    public sealed record Closure(Render.Person By, DateTimeOffset At);

    /// <summary>The Tangent the Topic is in: its slug, name and address.</summary>
    public sealed record InTangent(string Slug, string Name, string Address);

    /// <summary>What a link to the Topic, or to a Post in it, previews, where a visitor may read it, which is where it has
    /// <see cref="Feeds"/>: its title, its root's opening with no one named, none where the root is removed, its address, and
    /// the place's name, all read for the page. An article is said to be one, and its cover is the card's image, its
    /// render for a list, which previews large.</summary>
    public static SocialCard? Card(TopicPage page) => page.Feeds is null ? null : new SocialCard(
        page.Title,
        page.Root.Body is { } body ? TangentPage.Opening(Mentions.Unnamed(page.Root.Id, page.Root.Version, body)) : null,
        page.Cover is { } cover ? CardImage.Url(cover + "/" + MediaRecipes.CardName) : default,
        Url: page.Address,
        Type: page.Kind == PostWrites.Word(PostKind.Article) ? "article" : null,
        SiteName: page.Place,
        TwitterCard: page.Cover is null ? null : "summary_large_image");

    /// <summary>The version the page's <c>ETag</c> carries: a hash of everything the page says, so the same window for
    /// a visitor on the same day answers the same tag, and a reply, an edit, a removal, a changed <c>can</c> or a new day
    /// changes it. A signed-in reader's page carries the TIDs minted for their forms, new every time, so its version
    /// never repeats.</summary>
    public static long Version(TopicPage page)
        => Render.PageVersion.Of(page);
}
