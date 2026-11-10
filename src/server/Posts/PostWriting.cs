using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Events;
using Tangent.Participants;

namespace Tangent.Posts;

/// <summary>What every write that brings or changes a Post does past the door, in the write's hold: starting a Topic
/// (<see cref="Start"/>), replying (<see cref="Reply"/>) and editing (<see cref="Edit"/>), as the Topic and Post controllers'
/// own hooks run them (<see cref="TopicsController"/>, <see cref="PostsController"/>), and reporting (<see cref="ReportPost"/>).
/// Each fills the row it is handed and writes every row that follows from it; the row itself is its caller's to save, the
/// endpoint's insert or change, so a refusal of that save leaves nothing of the rest. Who may write was judged at the door,
/// and the body before the hold (<see cref="PostBody"/>).</summary>
public static class PostWriting
{
    /// <summary>Fills <paramref name="root"/>, which holds its Id and title, as the root of a Topic that starts in
    /// <paramref name="tangent"/> by <paramref name="authorId"/> at <paramref name="at"/>, of the Tangent's kind, with a slug
    /// from its title made unique there (<see cref="Slug.Unique"/>), taking <paramref name="origin"/> on a tangent where one is
    /// named, a Post its author may view that stands, and leading with <paramref name="cover"/>, an image its author may see,
    /// or a refusal with <see cref="Post.NoCover"/>; then writes what follows (<see cref="Wrote"/>), its event targeting the
    /// origin's author unless they take their own Post on a tangent.</summary>
    public static async Task Start(Post root, Tangents.Tangent tangent, PostBody body, string authorId, Post? origin, string? cover,
        DateTimeOffset at, CancellationToken ct)
    {
        root.OriginId = origin?.Id;
        root.StartIn(tangent, await Slug.Unique(tangent.Id, Slug.From(root.Title ?? ""), root.Id, ct), body, authorId, at);
        root.AuthorDid = await Participant.DidOf(authorId, ct);
        if (cover is not null)
        {
            if (!await MediaUse.Reaches(authorId, cover, ct)) throw new Refusal(Post.NoCover);
            root.Cover(cover);
        }

        await Wrote(root, root, tangent, origin is null || origin.AuthorId == authorId ? null : origin.AuthorId, at, ct);
    }

    /// <summary>Fills <paramref name="reply"/>, which holds its Id, as a reply by <paramref name="authorId"/> at
    /// <paramref name="at"/> to <paramref name="parent"/>, a Post of the Topic whose root is <paramref name="root"/>, as read
    /// in the hold, taking the Topic's next sequence (<see cref="Post.ReplyTo"/>); then writes what follows
    /// (<see cref="Wrote"/>), its event targeting the parent's author unless they answer their own Post.</summary>
    public static async Task Reply(Post reply, Post root, Post parent, PostBody body, string authorId, DateTimeOffset at, CancellationToken ct)
    {
        reply.ParentId = parent.Id;
        reply.ReplyTo(root, body, authorId, at);
        reply.AuthorDid = await Participant.DidOf(authorId, ct);
        await Wrote(reply, root, null, parent.AuthorId == authorId ? null : parent.AuthorId, at, ct);
    }

    /// <summary>Edits <paramref name="post"/>, the row to be saved, from <paramref name="prior"/>, the Post as it stands — the same
    /// row where nothing else holds a copy — by an edit made from <paramref name="version"/>, and answers whether it changed anything.
    /// An edit that would leave the Post as it is, or that made it what it is and is sent again (<see cref="Post.MadeBy"/>), changes
    /// nothing; one made from another version is refused as stale, naming the version it is at (<see cref="Post.Stale"/>). Otherwise
    /// the prior goes to the audit partition at its version, the Post takes the new words,
    /// the title where one is sent and <paramref name="cover"/> where one is sent, an image the editor may see or none
    /// (<see cref="Post.NoCover"/>), and its version rises (<see cref="Post.Edit"/>); what it uses moves with it, with the editor
    /// as who reaches it (<see cref="MediaUse.Record(Post, string, IReadOnlyList{string}, CancellationToken)"/>);
    /// <see cref="EventKind.PostEdited"/> is appended with the new version, targeting the author where someone else edited it;
    /// and one <see cref="EventKind.Mention"/> goes to each participant and role the new words mention and the old did not
    /// (<see cref="Mentions.Notify"/>).</summary>
    public static async Task<bool> Edit(Post post, Post prior, long version, PostBody body, string? title, string? cover, string editorId,
        DateTimeOffset at, CancellationToken ct)
    {
        if (prior.Unchanged(body.Text, title, cover) || prior.MadeBy(version, body, title, cover)) return false;
        if (prior.Version != version) throw new Refusal(Post.Stale(prior.Version));
        if (cover is { Length: > 0 } && cover != prior.CoverId && !await MediaUse.Reaches(editorId, cover, ct)) throw new Refusal(Post.NoCover);
        var before = prior.MentionedIds.Concat(prior.MentionedRoles).ToList();
        var used = prior.UsedMedia();
        await AuditPartition.Snapshot(prior, prior.Version, ct);
        post.Edit(body, title, editorId, at);
        if (cover is not null) post.Cover(cover);
        await MediaUse.Record(post, editorId, used, ct);
        await Event.Append(EventKind.PostEdited, at, ct,
            tangentId: post.TangentId, topicId: post.TopicId, postId: post.Id, actorId: editorId,
            targetId: editorId == post.AuthorId ? null : post.AuthorId, version: post.Version);
        await Mentions.Notify(post, editorId, before, at, ct);
        return true;
    }

    /// <summary>Writes what follows <paramref name="written"/>, a Post of the Topic whose root is <paramref name="root"/>, the root
    /// itself for a Topic that starts, by its author, all but the row itself: a Topic that starts is counted in
    /// <paramref name="tangent"/>, in all, in its month and by its author (<see cref="Tangents.Tangent.TopicCame"/>);
    /// <see cref="EventKind.PostCreated"/> is appended at <paramref name="at"/>, targeting <paramref name="targetId"/> — the author
    /// of what it answers, none for their own — and marked a report where <paramref name="report"/> says so
    /// (<see cref="Event.Report"/>); the Post and its root carry that row's sequence (<see cref="Post.Logged"/>), and a reply's root
    /// is saved with its counters; what the Post uses is recorded
    /// (<see cref="MediaUse.Record(Post, string, IReadOnlyList{string}, CancellationToken)"/>); the author's mark on the Topic moves
    /// onto the Post where it stood just before (<see cref="Seen.Wrote"/>) and, unless they report, they follow the Topic unless
    /// they stopped (<see cref="Follow.Wrote"/>); a reply is new to the Topic's other followers (<see cref="Follow.Moved"/>); and one
    /// <see cref="EventKind.Mention"/> goes to each participant and role the body mentions (<see cref="Mentions.Notify"/>).</summary>
    public static async Task Wrote(Post written, Post root, Tangents.Tangent? tangent, string? targetId, DateTimeOffset at,
        CancellationToken ct, bool report = false)
    {
        var author = written.AuthorId;
        if (written.ParentId is null)
        {
            var counted = tangent ?? throw new ArgumentNullException(nameof(tangent), "A Topic that starts is counted in its Tangent.");
            counted.TopicCame(written);
            await counted.Save(ct);
        }

        var created = await Event.Append(EventKind.PostCreated, at, ct,
            tangentId: written.TangentId, topicId: written.TopicId, postId: written.Id, actorId: author, targetId: targetId,
            version: written.Version, report: report ? true : null);
        written.Logged(created.Seq, root);
        if (written.ParentId is not null) await root.Save(ct);
        await MediaUse.Record(written, author, [], ct);
        var caughtUp = await Seen.Wrote(author, root, written.Sequence, ct);
        if (!report) await Follow.Wrote(author, root, caughtUp, ct);
        if (written.ParentId is not null) await Follow.Moved(root, author, ct);
        await Mentions.Notify(written, author, [], at, ct);
    }
}
