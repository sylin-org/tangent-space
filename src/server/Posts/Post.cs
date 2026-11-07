using System.Text;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;

namespace Tangent.Posts;

/// <summary>One message, a root or a reply, keyed by the TID the client minted. A Topic has no row: its title, slug,
/// kind, cover and counters live on its root Post, whose <see cref="TopicId"/> is its own Id. A reply carries its
/// root's <see cref="TangentId"/> and <see cref="TopicId"/> and the next <see cref="Sequence"/> of its Topic. Every
/// Post carries <see cref="LogSeq"/>, where the log took its arrival, and its root <see cref="LastLogSeq"/>, its
/// newest Post's, so a list of Topics says what is new with no further read (<see cref="Events.Unread"/>).</summary>
public sealed class Post : Entity<Post>
{
    /// <summary>How many of a Topic's most recent authors its root keeps.</summary>
    public const int RecentAuthorLimit = 4;

    /// <summary>The most UTF-8 bytes a body holds as its sender sent it.</summary>
    public const int BodyLimit = 64 * 1024;

    /// <summary>The most UTF-8 bytes a body holds as the Post keeps it: what was sent, within <see cref="BodyLimit"/>,
    /// grown by at most <see cref="Mentions.Limit"/> typed handles written as mentions, each adding at most
    /// <see cref="Mentions.TypedWeight"/>, as many typed Tangents written as links, each adding at most
    /// <see cref="Mentions.LinkedWeight"/>, and as many typed roles written as mentions, each adding at most
    /// <see cref="Mentions.RoleWeight"/>.</summary>
    public const int KeptBodyLimit = BodyLimit + Mentions.Limit * (Mentions.TypedWeight + Mentions.LinkedWeight + Mentions.RoleWeight);

    public const int TitleLimit = 200;

    public static readonly Reason NeedsTid = Reason.Invalid("A Post's Id is a TID.");
    public static readonly Reason NeedsBody = Reason.Invalid("A Post's body holds at least one visible character.");
    public static readonly Reason BodyTooLong = Reason.Invalid("A Post's body holds at most 64 KiB of UTF-8.");
    public static readonly Reason BodyHasNul = Reason.Invalid("A Post's body holds no NUL character.");
    public static readonly Reason BodyRendersTooLong = Reason.Invalid("A Post's body renders to at most 512 KiB of HTML, its elements nested no deeper than the renderer allows.");
    public static readonly Reason TooManyMentions = Reason.Invalid("A Post's body mentions at most 32 identities.");
    public static readonly Reason BodyNotJudged = Reason.Invalid("This body was not judged before it was sent; send it again.");
    public static readonly Reason BodyTooIntricate = Reason.Invalid("A Post's body holds too many unclosed links, brackets or emphasis marks to render quickly.");
    public static readonly Reason TooManyImages = Reason.Invalid("A Post's body holds at most 64 images of this place.");
    public static readonly Reason NoCover = Reason.Invalid("This cover isn't an image you can use here.");
    public static readonly Reason CoverOnArticle = Reason.Invalid("Only an article has a cover.");
    public static readonly Reason NeedsTitle = Reason.Invalid("A Topic's title holds at least one visible character.");
    public static readonly Reason TitleTooLong = Reason.Invalid("A Topic's title holds at most 200 characters.");
    public static readonly Reason NeedsTangent = Reason.Invalid("A Topic starts in a Tangent.");
    public static readonly Reason NeedsAuthor = Reason.Invalid("A Post has an author.");
    public static readonly Reason NotARoot = Reason.Invalid("A reply goes to a Topic.");
    public static readonly Reason TopicRemoved = Access.Capabilities.RootRemoved;
    public static readonly Reason NoTitleOnReply = Reason.Invalid("A reply has no title; a Topic's title is on its root.");
    public static readonly Reason PostRemoved = Access.Capabilities.Removed;
    public static readonly Reason NeedsRemover = Reason.Invalid("A removal names who removes.");
    public static readonly Reason NotRemoved = Reason.Invalid("Only a removed post comes back.");

    /// <summary>The one answer for an Id a Post already holds that is not this request's, word <c>id-taken</c>: a retry
    /// learns its Post was written, and nothing says where, whose or what that Post is.</summary>
    public static readonly Reason IdTaken = Reason.Conflict("id-taken", "A Post already has this id; if it is yours, it was written. Mint another id to post anything new.");

    /// <summary>The Tangent the Topic is in; on every row.</summary>
    [Index(Name = "ix_post_topics", Order = 0)]
    [Index(Name = "ix_post_slugs", Order = 0)]
    [Index(Name = "ix_post_started", Order = 0)]
    [Index(Name = "ix_post_topics_by", Order = 0)]
    public string TangentId { get; set; } = "";

    /// <summary>The root of the Topic; the root's own Id on a root.</summary>
    [Index(Name = "ix_post_windows", Order = 0)]
    public string TopicId { get; set; } = "";

    /// <summary>The most images of this place one body names, each a row while the Post uses it (<see cref="MediaUse"/>).</summary>
    public const int MediaLimit = 64;

    /// <summary>The Post this one replies to; null on a root.</summary>
    [Index(Name = "ix_post_topics", Order = 1)]
    [Index(Name = "ix_post_started", Order = 1)]
    [Index(Name = "ix_post_topics_by", Order = 1)]
    [Index(Name = "ix_post_articles", Order = 1)]
    public string? ParentId { get; set; }

    /// <summary>The Post this Topic was taken on a tangent from; null otherwise. Root only. In <c>ix_post_origins</c>
    /// before the time the Topic started, so the Topics taken from one Post, newest first, are one range of it.</summary>
    [Index(Name = "ix_post_origins", Order = 0)]
    public string? OriginId { get; set; }

    /// <summary>The Topic's URL name, from its title at creation, unique in its Tangent and never renamed, except that a Topic
    /// moved into a Tangent that holds it takes a numbered suffix there (<see cref="MoveTopic"/>). Root only.</summary>
    [Index(Name = "ix_post_slugs", Order = 1)]
    public string? Slug { get; set; }

    /// <summary>Root only; a reply's Topic has the kind of its root.</summary>
    [Index(Name = "ix_post_articles", Order = 0)]
    public PostKind Kind { get; set; }

    /// <summary>Root only. Found by its words.</summary>
    [Lexical]
    public string? Title { get; set; }

    /// <summary>An article's lead image. Root only.</summary>
    public string? CoverId { get; set; }

    /// <summary>Markdown, rendered to safe HTML where it is shown (<see cref="MarkdownHtml"/>); a mention is a link to
    /// <c>/p/</c> and a participant's id (<see cref="Mentions"/>). Found by its words.</summary>
    [Lexical]
    public string Body { get; set; } = "";

    /// <summary>The <see cref="Media"/> the body's images name, each once, in the order of their first image, at most
    /// <see cref="MediaLimit"/>; empty on a tombstone.</summary>
    public List<string> MediaIds { get; set; } = [];

    /// <summary>The ids of the participants the body mentions, each once, in the order of their first mention, at most
    /// <see cref="Mentions.Limit"/>; empty on a tombstone.</summary>
    public List<string> MentionedIds { get; set; } = [];

    /// <summary>The roles the body mentions, by key, each once, in the order typed, at most <see cref="Mentions.Limit"/>;
    /// empty on a tombstone.</summary>
    public List<string> MentionedRoles { get; set; } = [];

    /// <summary>The body as the request that last wrote it sent it, as <see cref="PostBody.SentHash"/>: read only to
    /// recognize that request sent again, whoever its typed handles name by then; empty on a tombstone.</summary>
    public string SentHash { get; set; } = "";

    /// <summary>Raised by every edit.</summary>
    public long Version { get; set; } = 1;

    /// <summary>The Post's place in its Topic: zero on the root, then one higher per reply.</summary>
    [Index(Name = "ix_post_windows", Order = 1)]
    public long Sequence { get; set; }

    /// <summary>Who wrote it. In <c>ix_post_authors</c> before the time it was written, so a participant's Posts,
    /// newest first, are one range of it, and in <c>ix_post_topics_by</c> before a Topic's last activity, so the Topics
    /// one author started in a Tangent, newest activity first, are one range.</summary>
    [Index(Name = "ix_post_authors", Order = 0)]
    [Index(Name = "ix_post_topics_by", Order = 2)]
    public string AuthorId { get; set; } = "";

    /// <summary>The DID of the Bluesky account its author was bound to when they wrote it, which an edit never changes:
    /// the account the Post links out to and goes out under.</summary>
    public string AuthorDid { get; set; } = "";

    [Index(Name = "ix_post_origins", Order = 1)]
    [Index(Name = "ix_post_authors", Order = 1)]
    [Index(Name = "ix_post_started", Order = 3)]
    [Index(Name = "ix_post_articles", Order = 3)]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The last sequence taken in the Topic. Root only.</summary>
    public long LastSequence { get; set; }

    /// <summary>The log's sequence of the <see cref="Events.EventKind.PostCreated"/> that brought this Post, written by
    /// the command that writes the Post.</summary>
    public long LogSeq { get; set; }

    /// <summary>The <see cref="LogSeq"/> of the Topic's newest Post. Root only.</summary>
    public long LastLogSeq { get; set; }

    /// <summary>Root only.</summary>
    public int ReplyCount { get; set; }

    /// <summary>When the Topic last gained a Post. Root only.</summary>
    [Index(Name = "ix_post_topics", Order = 2)]
    [Index(Name = "ix_post_topics_by", Order = 3)]
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>The Topic's latest reply and its author; null until the first reply. Root only.</summary>
    public string? LatestReplyId { get; set; }

    public string? LatestReplyAuthorId { get; set; }

    /// <summary>The Topic's most recent authors, newest first, each once, at most <see cref="RecentAuthorLimit"/>.
    /// Root only.</summary>
    public List<string> RecentAuthorIds { get; set; } = [];

    /// <summary>In <c>ix_post_started</c> and <c>ix_post_articles</c> before the time a Topic started, so a Tangent's
    /// Topics or the articles that stand, newest first, are each one range, however many were removed.</summary>
    [Index(Name = "ix_post_started", Order = 2)]
    [Index(Name = "ix_post_articles", Order = 2)]
    public bool Removed { get; set; }

    public string? RemovedBy { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }

    /// <summary>Whether the Topic is closed: it takes no new reply but from whoever holds <see cref="Access.Bit.Moderate"/>
    /// in its Tangent (<see cref="Access.Capabilities.Closed"/>), and who closed it and when. Root only; a removal leaves it
    /// as it is.</summary>
    public bool Closed { get; set; }

    public string? ClosedBy { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Who other than the author last edited the Post, while the words are theirs; null otherwise. An edit by
    /// the author clears it, and so does a removal.</summary>
    public string? EditedBy { get; set; }

    public DateTimeOffset? EditedAt { get; set; }

    /// <summary>When the Post's words last changed, by anyone; null until its first edit. A feed says it.</summary>
    public DateTimeOffset? ChangedAt { get; set; }

    /// <summary>A Topic's root, or a refusal: the Id is a TID, the title and body are within their limits, and the Tangent
    /// and author are named. The slug is the caller's, derived from the title and already made unique in the Tangent. A
    /// Topic taken on a tangent names its <paramref name="originId"/>, already known to be a Post its author may view, and
    /// a body of its author's own is their choice: one of nothing but white space is none, and the root holds an empty
    /// body (<see cref="RootBody"/>).</summary>
    public static Post Root(string id, string tangentId, string title, string slug, PostKind kind, PostBody body, string authorId, DateTimeOffset at,
        string? originId = null)
    {
        if (!Tid.IsValid(id)) throw new Refusal(NeedsTid);
        if (string.IsNullOrWhiteSpace(tangentId)) throw new Refusal(NeedsTangent);
        if (string.IsNullOrWhiteSpace(authorId)) throw new Refusal(NeedsAuthor);
        CheckTitle(title);
        var text = RootBody(body, originId);
        if (originId is null || text.Length > 0) CheckBody(body);
        return new Post
        {
            Id = id,
            TangentId = tangentId,
            TopicId = id,
            OriginId = originId,
            Slug = slug,
            Kind = kind,
            Title = title.Trim(),
            Body = text,
            MentionedIds = text.Length > 0 ? [.. body.Mentioned] : [],
            MentionedRoles = text.Length > 0 ? [.. body.Roles] : [],
            MediaIds = text.Length > 0 ? [.. body.Media] : [],
            SentHash = body.SentHash,
            AuthorId = authorId,
            CreatedAt = at,
            LastActivityAt = at,
            RecentAuthorIds = [authorId]
        };
    }

    /// <summary>The body a root starts with: the body sent, except that a Topic taken on a tangent from
    /// <paramref name="originId"/> may start with none, and a body of nothing but white space is none.</summary>
    public static string RootBody(PostBody body, string? originId)
        => originId is not null && string.IsNullOrWhiteSpace(body.Text) ? "" : body.Text;

    /// <summary>A reply to this root, or a refusal: this Post is a root that is not removed, the Id is a TID,
    /// the body is within its limits and the author is named. The parent is the caller's, already known to be a Post of
    /// this Topic. The reply takes the Topic's next sequence, and the root's counters, last activity, latest reply and
    /// recent authors move with it.</summary>
    public Post Reply(string id, PostBody body, string authorId, string parentId, DateTimeOffset at)
    {
        if (ParentId is not null) throw new Refusal(NotARoot);
        if (Removed) throw new Refusal(TopicRemoved);
        if (!Tid.IsValid(id)) throw new Refusal(NeedsTid);
        if (string.IsNullOrWhiteSpace(authorId)) throw new Refusal(NeedsAuthor);
        CheckBody(body);
        LastSequence = checked(LastSequence + 1);
        ReplyCount = checked(ReplyCount + 1);
        LastActivityAt = at;
        LatestReplyId = id;
        LatestReplyAuthorId = authorId;
        RecentAuthorIds = [authorId, .. RecentAuthorIds.Where(author => author != authorId).Take(RecentAuthorLimit - 1)];
        return new Post
        {
            Id = id,
            TangentId = TangentId,
            TopicId = Id,
            ParentId = parentId,
            Body = body.Text,
            MentionedIds = [.. body.Mentioned],
            MentionedRoles = [.. body.Roles],
            MediaIds = [.. body.Media],
            SentHash = body.SentHash,
            Sequence = LastSequence,
            AuthorId = authorId,
            CreatedAt = at
        };
    }

    /// <summary>Records that the log took this Post's arrival at <paramref name="logSeq"/>, on it and on its Topic's
    /// <paramref name="root"/>, whose newest Post it is: the root itself, for a Topic that starts.</summary>
    public void Logged(long logSeq, Post root)
    {
        LogSeq = logSeq;
        root.LastLogSeq = logSeq;
    }

    /// <summary>Whether an edit with this body, title and cover would leave the Post as it is: the same body, on a root
    /// the same title or none sent, and the same cover or none sent; a reply sent no title.</summary>
    public bool Unchanged(string body, string? title, string? cover = null) => Body == body && Titled(title) && Covered(cover);

    /// <summary>Whether an edit made from <paramref name="version"/> with this body and title is the one that made the
    /// Post what it is, sent again: it took the Post from that version to this one with the same words sent, and on a
    /// root the same title or none, and the same cover or none; a reply sent no title. Whoever its typed handles name now
    /// changes nothing.</summary>
    public bool MadeBy(long version, PostBody body, string? title, string? cover = null)
        => Version == version + 1 && SentHash == body.SentHash && Titled(title) && Covered(cover);

    /// <summary>Whether this cover is the one the Post has, an empty one meaning none; true where none was sent.</summary>
    private bool Covered(string? cover) => cover is null || (cover.Length == 0 ? CoverId is null : CoverId == cover);

    /// <summary>Every piece of <see cref="Media"/> the Post uses now: its cover and the images of its body.</summary>
    public IReadOnlyList<string> UsedMedia() => CoverId is { } cover ? [.. MediaIds.Prepend(cover).Distinct(StringComparer.Ordinal)] : [.. MediaIds];

    /// <summary>Gives an article <paramref name="cover"/>, or takes its cover away where that is empty, or a refusal: only
    /// an article's root has one.</summary>
    public void Cover(string cover)
    {
        if (cover.Length > 0 && (ParentId is not null || Kind != PostKind.Article)) throw new Refusal(CoverOnArticle);
        CoverId = cover.Length > 0 ? cover : null;
    }

    private bool Titled(string? title) => ParentId is null ? title is null || Title == title.Trim() : string.IsNullOrEmpty(title);

    /// <summary>Changes the body, and a root's title when one is sent, and raises <see cref="Version"/>, or a refusal: the
    /// Post is not removed, the body and title are within their limits, and a reply is sent no title. A root taken on a
    /// tangent may keep no body of its author's own (<see cref="RootBody"/>); every other Post needs one. An edit by
    /// anyone but the author names them and when (<see cref="EditedBy"/>), and the author's own leaves the words theirs
    /// again. The pre-edit snapshot is the caller's, taken before this.</summary>
    public void Edit(PostBody body, string? title, string editorId, DateTimeOffset at)
    {
        if (Removed) throw new Refusal(PostRemoved);
        var text = ParentId is null ? RootBody(body, OriginId) : body.Text;
        if (OriginId is null || ParentId is not null || text.Length > 0) CheckBody(body);
        if (ParentId is not null && !string.IsNullOrEmpty(title)) throw new Refusal(NoTitleOnReply);
        if (ParentId is null && title is not null)
        {
            CheckTitle(title);
            Title = title.Trim();
        }

        Body = text;
        MentionedIds = text.Length > 0 ? [.. body.Mentioned] : [];
        MentionedRoles = text.Length > 0 ? [.. body.Roles] : [];
        MediaIds = text.Length > 0 ? [.. body.Media] : [];
        SentHash = body.SentHash;
        var someoneElse = editorId != AuthorId;
        EditedBy = someoneElse ? editorId : null;
        EditedAt = someoneElse ? at : null;
        ChangedAt = at;
        Version = checked(Version + 1);
    }

    /// <summary>Makes this Post a tombstone, or a refusal when it already is one or no one is named: it is removed, by
    /// whom and when, and its body, and who last edited it, are cleared from the row. It keeps its place, its author and
    /// its version, and a root keeps its title, slug and counters, so a Topic whose root is removed keeps its replies. The
    /// snapshot that holds the body is the caller's, taken before this, and <see cref="Back"/> reads it back.</summary>
    public void Remove(string removedBy, DateTimeOffset at)
    {
        if (Removed) throw new Refusal(PostRemoved);
        if (string.IsNullOrWhiteSpace(removedBy)) throw new Refusal(NeedsRemover);
        Removed = true;
        RemovedBy = removedBy;
        RemovedAt = at;
        Body = "";
        MentionedIds = [];
        MentionedRoles = [];
        MediaIds = [];
        CoverId = null;
        SentHash = "";
        EditedBy = null;
        EditedAt = null;
    }

    /// <summary>Brings a tombstone's words back from <paramref name="kept"/>, its snapshot at its version, or a refusal where it
    /// is not removed: no longer removed, it takes its body, mentions, images, cover and who last edited it from the snapshot,
    /// and keeps its version and when its words last changed, since the snapshot is that version.</summary>
    public void Back(Post kept)
    {
        if (!Removed) throw new Refusal(NotRemoved);
        (Removed, RemovedBy, RemovedAt) = (false, null, null);
        Body = kept.Body;
        MentionedIds = [.. kept.MentionedIds];
        MentionedRoles = [.. kept.MentionedRoles];
        MediaIds = [.. kept.MediaIds];
        CoverId = kept.CoverId;
        SentHash = kept.SentHash;
        EditedBy = kept.EditedBy;
        EditedAt = kept.EditedAt;
    }

    /// <summary>Closes this Topic to replies, by whom and when, or a refusal: this Post is a root that is not removed.
    /// Answers whether anything changed: a closed Topic stays as it was closed. Every word stays, and its version with them.</summary>
    public bool Close(string closedBy, DateTimeOffset at)
    {
        if (ParentId is not null) throw new Refusal(Posts.Reply.NoTopic);
        if (Removed) throw new Refusal(PostRemoved);
        if (Closed) return false;
        (Closed, ClosedBy, ClosedAt) = (true, closedBy, at);
        return true;
    }

    /// <summary>Opens this Topic to replies again, or a refusal: this Post is a root. Answers whether anything changed.</summary>
    public bool Reopen()
    {
        if (ParentId is not null) throw new Refusal(Posts.Reply.NoTopic);
        if (!Closed) return false;
        (Closed, ClosedBy, ClosedAt) = (false, null, null);
        return true;
    }

    /// <summary>Refuses a body that is empty, holds a NUL, was sent with more than <see cref="BodyLimit"/> bytes or would
    /// be kept with more than <see cref="KeptBodyLimit"/>, or that its own judgement refused
    /// (<see cref="PostBody.Refusal"/>): too intricate to render quickly, rendering too large or too deep, or mentioning
    /// too many identities.</summary>
    public static void CheckBody(PostBody body)
    {
        if (string.IsNullOrWhiteSpace(body.Text)) throw new Refusal(NeedsBody);
        if (body.Text.Contains('\0')) throw new Refusal(BodyHasNul);
        if (Encoding.UTF8.GetByteCount(body.Sent) > BodyLimit || Encoding.UTF8.GetByteCount(body.Text) > KeptBodyLimit)
            throw new Refusal(BodyTooLong);
        if (body.Refusal is { } refusal) throw new Refusal(refusal);
    }

    public static void CheckTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new Refusal(NeedsTitle);
        if (title.Trim().Length > TitleLimit) throw new Refusal(TitleTooLong);
    }
}
