using System.Text;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>One message, a root or a reply, keyed by the TID the client minted. A Topic has no row: its title, slug,
/// kind, cover, lock, pin and counters live on its root Post, whose <see cref="TopicId"/> is its own Id. A reply
/// carries its root's <see cref="TangentId"/> and <see cref="TopicId"/> and the next <see cref="Sequence"/> of its
/// Topic.</summary>
public sealed class Post : Entity<Post>
{
    /// <summary>How many of a Topic's most recent authors its root keeps.</summary>
    public const int RecentAuthorLimit = 4;

    /// <summary>The most UTF-8 bytes a body holds as its sender sent it.</summary>
    public const int BodyLimit = 64 * 1024;

    /// <summary>The most UTF-8 bytes a body holds as the Post keeps it: what was sent, within <see cref="BodyLimit"/>,
    /// grown by at most <see cref="Mentions.Limit"/> typed handles written as mentions, each adding at most
    /// <see cref="Mentions.TypedWeight"/>.</summary>
    public const int KeptBodyLimit = BodyLimit + Mentions.Limit * Mentions.TypedWeight;

    public const int TitleLimit = 200;

    public const string NeedsTid = "A Post's Id is a TID.";
    public const string NeedsBody = "A Post's body holds at least one visible character.";
    public const string BodyTooLong = "A Post's body holds at most 64 KiB of UTF-8.";
    public const string BodyHasNul = "A Post's body holds no NUL character.";
    public const string BodyRendersTooLong = "A Post's body renders to at most 512 KiB of HTML, its elements nested no deeper than the renderer allows.";
    public const string TooManyMentions = "A Post's body mentions at most 32 identities.";
    public const string BodyNotJudged = "This body was not judged before it was sent; send it again.";
    public const string BodyTooIntricate = "A Post's body holds too many unclosed links, brackets or emphasis marks to render quickly.";
    public const string NeedsTitle = "A Topic's title holds at least one visible character.";
    public const string TitleTooLong = "A Topic's title holds at most 200 characters.";
    public const string NeedsTangent = "A Topic starts in a Tangent.";
    public const string NeedsAuthor = "A Post has an author.";
    public const string NotARoot = "A reply goes to a Topic.";
    public const string TopicLocked = "The Topic is locked.";
    public const string TopicRemoved = "The Topic is removed.";
    public const string NoTitleOnReply = "A reply has no title; a Topic's title is on its root.";
    public const string PostRemoved = "The Post is removed.";
    public const string NeedsRemover = "A removal names who removes.";

    /// <summary>The one answer for an Id a Post outside the place a command names already holds: it says nothing of
    /// where, whose or what that Post is.</summary>
    public const string IdTaken = "This id is not available; mint another.";

    /// <summary>The Tangent the Topic is in; on every row.</summary>
    [Index(Name = "ix_post_topics", Order = 0)]
    [Index(Name = "ix_post_slugs", Order = 0)]
    public string TangentId { get; set; } = "";

    /// <summary>The root of the Topic; the root's own Id on a root.</summary>
    [Index(Name = "ix_post_windows", Order = 0)]
    public string TopicId { get; set; } = "";

    /// <summary>The Post this one replies to; null on a root.</summary>
    [Index(Name = "ix_post_topics", Order = 1)]
    public string? ParentId { get; set; }

    /// <summary>The Post this Topic was taken on a tangent from; null otherwise. Root only. In <c>ix_post_origins</c>
    /// before the time the Topic started, so the Topics taken from one Post, newest first, are one range of it.</summary>
    [Index(Name = "ix_post_origins", Order = 0)]
    public string? OriginId { get; set; }

    /// <summary>The Topic's URL name, from its title at creation, unique in its Tangent and never renamed. Root only.</summary>
    [Index(Name = "ix_post_slugs", Order = 1)]
    public string? Slug { get; set; }

    /// <summary>Root only; a reply's Topic has the kind of its root.</summary>
    public PostKind Kind { get; set; }

    /// <summary>Root only. Found by its words.</summary>
    [Lexical]
    public string? Title { get; set; }

    /// <summary>An article's lead image. Root only.</summary>
    public string? CoverId { get; set; }

    /// <summary>Markdown, rendered to safe HTML where it is shown (<see cref="MarkdownHtml"/>); a mention is a link to
    /// <c>at://</c> and a DID (<see cref="Mentions"/>). Found by its words.</summary>
    [Lexical]
    public string Body { get; set; } = "";

    /// <summary>The DIDs the body mentions, each once, in the order of their first mention, at most
    /// <see cref="Mentions.Limit"/>; empty on a tombstone.</summary>
    public List<string> MentionedIds { get; set; } = [];

    /// <summary>The body as the request that last wrote it sent it, as <see cref="PostBody.SentHash"/>: read only to
    /// recognize that request sent again, whoever its typed handles name by then; empty on a tombstone.</summary>
    public string SentHash { get; set; } = "";

    /// <summary>Raised by every edit.</summary>
    public long Version { get; set; } = 1;

    /// <summary>The Post's place in its Topic: zero on the root, then one higher per reply.</summary>
    [Index(Name = "ix_post_windows", Order = 1)]
    public long Sequence { get; set; }

    /// <summary>Who wrote it. In <c>ix_post_authors</c> before the time it was written, so a participant's Posts,
    /// newest first, are one range of it.</summary>
    [Index(Name = "ix_post_authors", Order = 0)]
    public string AuthorId { get; set; } = "";

    [Index(Name = "ix_post_origins", Order = 1)]
    [Index(Name = "ix_post_authors", Order = 1)]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The last sequence taken in the Topic. Root only.</summary>
    public long LastSequence { get; set; }

    /// <summary>Root only.</summary>
    public int ReplyCount { get; set; }

    /// <summary>When the Topic last gained a Post. Root only.</summary>
    [Index(Name = "ix_post_topics", Order = 3)]
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>The Topic's latest reply and its author; null until the first reply. Root only.</summary>
    public string? LatestReplyId { get; set; }

    public string? LatestReplyAuthorId { get; set; }

    /// <summary>The Topic's most recent authors, newest first, each once, at most <see cref="RecentAuthorLimit"/>.
    /// Root only.</summary>
    public List<string> RecentAuthorIds { get; set; } = [];

    /// <summary>Root only.</summary>
    public bool Locked { get; set; }

    /// <summary>Root only. In the topic-list index before the last activity, so the pinned Topics and the others are
    /// each one range of it.</summary>
    [Index(Name = "ix_post_topics", Order = 2)]
    public bool Pinned { get; set; }

    public bool Removed { get; set; }

    public string? RemovedBy { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }

    /// <summary>Who other than the author last edited the Post, while the words are theirs; null otherwise. An edit by
    /// the author clears it, and so does a removal.</summary>
    public string? EditedBy { get; set; }

    public DateTimeOffset? EditedAt { get; set; }

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

    /// <summary>A reply to this root, or a refusal: this Post is a root that is neither locked nor removed, the Id is a TID,
    /// the body is within its limits and the author is named. The parent is the caller's, already known to be a Post of
    /// this Topic. The reply takes the Topic's next sequence, and the root's counters, last activity, latest reply and
    /// recent authors move with it.</summary>
    public Post Reply(string id, PostBody body, string authorId, string parentId, DateTimeOffset at)
    {
        if (ParentId is not null) throw new Refusal(NotARoot);
        if (Removed) throw new Refusal(TopicRemoved);
        if (Locked) throw new Refusal(TopicLocked);
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
            SentHash = body.SentHash,
            Sequence = LastSequence,
            AuthorId = authorId,
            CreatedAt = at
        };
    }

    /// <summary>Whether an edit with this body and title would leave the Post as it is: the same body, and on a root the
    /// same title or none sent; a reply sent no title.</summary>
    public bool Unchanged(string body, string? title) => Body == body && Titled(title);

    /// <summary>Whether an edit made from <paramref name="version"/> with this body and title is the one that made the
    /// Post what it is, sent again: it took the Post from that version to this one with the same words sent, and on a
    /// root the same title or none; a reply sent no title. Whoever its typed handles name now changes nothing.</summary>
    public bool MadeBy(long version, PostBody body, string? title) => Version == version + 1 && SentHash == body.SentHash && Titled(title);

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
        SentHash = body.SentHash;
        var someoneElse = editorId != AuthorId;
        EditedBy = someoneElse ? editorId : null;
        EditedAt = someoneElse ? at : null;
        Version = checked(Version + 1);
    }

    /// <summary>Makes this Post a tombstone, or a refusal when it already is one or no one is named: it is removed, by
    /// whom and when, and its body, and who last edited it, are cleared from the row. It keeps its place, its author and
    /// its version, and a root keeps its title, slug and counters, so a Topic whose root is removed keeps its replies. The
    /// snapshot that holds the body is the caller's, taken before this.</summary>
    public void Remove(string removedBy, DateTimeOffset at)
    {
        if (Removed) throw new Refusal(PostRemoved);
        if (string.IsNullOrWhiteSpace(removedBy)) throw new Refusal(NeedsRemover);
        Removed = true;
        RemovedBy = removedBy;
        RemovedAt = at;
        Body = "";
        MentionedIds = [];
        SentHash = "";
        EditedBy = null;
        EditedAt = null;
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
