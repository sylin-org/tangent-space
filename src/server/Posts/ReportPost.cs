using Koan.Data.Abstractions;
using Koan.Data.Core;
using Tangent.Access;
using Tangent.Events;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Reports a Post: takes it on a tangent into the moderators' Tangent (<see cref="ModeratorsTangent"/>) as a
/// Topic whose root carries the reported Post as its origin, the reporter as its author, a title (<see cref="Title"/>)
/// and the reason as its body. The title names the reported Topic only where the moderator role may view the reported
/// Tangent (<see cref="ModeratorsTangent.MayView"/>); elsewhere it is the reason's first words as sent, so nothing of
/// a Tangent the moderators may not view reaches theirs. Reporting is its own verb, held by every participant for
/// whom the Post exists, whatever they hold in the moderators' Tangent: the Post exists for the reporter, who holds
/// <see cref="Bit.View"/> where it is, or the command answers that no Post has the Id before anything else; a visitor is
/// refused with <see cref="Capabilities.SignIn"/>; a Post of the moderators' Tangent with <see cref="InModerators"/>; a
/// removed Post with <see cref="Capabilities.Removed"/>; a reason of
/// nothing but white space with <see cref="NeedsReason"/>. The <see cref="EventKind.PostCreated"/> it appends targets
/// nobody, so the reported author hears nothing of it, and is marked a report (<see cref="Event.Report"/>), which is how it
/// reaches everyone who may view the moderators' Tangent when they read their notifications, without one row written for
/// each of them (<see cref="Notifications"/>); and one <see cref="EventKind.Mention"/> goes to each participant
/// the reason mentions who may view the moderators' Tangent (<see cref="Mentions.Notify"/>). A participant who reports
/// the same Post again while their earlier report stands — its root neither removed nor locked — replies in that
/// report's Topic with the new reason, so the moderators keep one Topic per reporter and Post with every reason in
/// order; once the moderators remove or lock it, the next report starts a Topic of its own. Idempotent by Id: the same
/// Id from the same reporter about the same Post answers with the Post it wrote, whatever reason is sent and whatever
/// the moderators have done with it since, so a resend says nothing of the report's state; any other Post holding it is
/// refused with <see cref="Post.IdTaken"/>, which says nothing of that Post. The reason is judged where the command is built
/// (<see cref="PostBody"/>), never in the writer.</summary>
public sealed record ReportPost(
    string Id,
    string PostId,
    PostBody Reason,
    string ReporterId,
    DateTimeOffset? At = null) : ICommand<Post>
{
    public const string NoPost = "No Post has this Id.";

    public const string NeedsReason = "Say in a sentence or two why you are reporting this post.";

    /// <summary>The answer where the place records no moderators' Tangent, which a start creates.</summary>
    public const string NoModerators = "This Tangent Space has no moderators' Tangent yet, so the report was not sent.";

    /// <summary>The answer for a Post of the moderators' Tangent, which is already where the moderators read.</summary>
    public const string InModerators = "Posts in the moderators' Tangent are not reported; the moderators already read them there.";

    /// <summary>What a report's title starts with, before the reported Topic's title or the reason's first words.</summary>
    public const string TitlePrefix = "Report: ";

    /// <summary>The most characters of the reason a title carries where it names no Topic.</summary>
    public const int ReasonWords = 60;

    public async Task<Outcome<Post>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Post.Get(PostId, ct) is not { } reported) return new Outcome<Post>.Refused(NoPost);
        var topic = reported.ParentId is null ? reported : await Post.Get(reported.TopicId, ct);
        if (topic is null) return new Outcome<Post>.Refused(NoPost);
        var rows = await Capabilities.LoadRows(ReporterId, reported.TangentId, ct);
        if (!rows.For(reported, reported.ParentId is null ? null : topic).Exists) return new Outcome<Post>.Refused(NoPost);
        if (rows.Participant is null) return new Outcome<Post>.Refused(Capabilities.SignIn);

        if (rows.Space.ModeratorsTangentId is not { } moderators || await Tangents.Tangent.Get(moderators, ct) is not { } into)
            return new Outcome<Post>.Refused(NoModerators);
        if (await Post.Get(Id, ct) is { } existing)
        {
            return await IsReport(existing, moderators, ReporterId, PostId, ct)
                ? new Outcome<Post>.Done(existing)
                : new Outcome<Post>.Refused(Post.IdTaken);
        }

        if (reported.TangentId == moderators) return new Outcome<Post>.Refused(InModerators);

        if (reported.Removed) return new Outcome<Post>.Refused(Capabilities.Removed);
        if (string.IsNullOrWhiteSpace(Reason.Text)) return new Outcome<Post>.Refused(NeedsReason);

        var at = At ?? DateTimeOffset.UtcNow;
        Post written;
        if (await Standing(moderators, ct) is { } earlier)
        {
            written = earlier.Reply(Id, Reason, ReporterId, earlier.Id, at);
            await written.Save(ct);
            await earlier.Save(ct);
        }
        else
        {
            var title = Title(await ModeratorsTangent.MayView(reported.TangentId, rows.Space, ct) ? topic.Title ?? "" : FirstWords(Reason.Sent));
            written = Post.Root(Id, into.Id, title, await StartTopic.UniqueSlug(into.Id, title, Id, ct), into.DefaultKind, Reason, ReporterId, at,
                reported.Id);
            await written.Save(ct);
            into.TopicStarted();
            await into.Save(ct);
        }

        await Event.Next(sequences, EventKind.PostCreated, at,
            tangentId: written.TangentId, topicId: written.TopicId, postId: written.Id, actorId: ReporterId, version: written.Version,
            report: true).Save(ct);
        await Mentions.Notify(sequences, written, ReporterId, [], at, ct);
        return new Outcome<Post>.Done(written);
    }

    /// <summary>A report's title: <see cref="TitlePrefix"/> and what it names, cut short with an ellipsis where the
    /// whole would pass <see cref="Post.TitleLimit"/>.</summary>
    public static string Title(string named)
    {
        var whole = TitlePrefix + named.Trim();
        return whole.Length <= Post.TitleLimit ? whole : whole[..(Post.TitleLimit - 1)].TrimEnd() + "…";
    }

    /// <summary>The reason's first words, on one line, cut at a word within <see cref="ReasonWords"/> characters.</summary>
    public static string FirstWords(string reason)
    {
        var line = string.Join(' ', reason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length <= ReasonWords) return line;
        var cut = line.LastIndexOf(' ', ReasonWords);
        return line[..(cut > 0 ? cut : ReasonWords)].TrimEnd() + "…";
    }

    /// <summary>Whether <paramref name="existing"/> is a report <paramref name="reporterId"/> made of the Post
    /// <paramref name="postId"/>: a Post of the moderators' Tangent they wrote whose Topic's root has that Post as its
    /// origin. At most one keyed read, of a reply's root.</summary>
    public static async Task<bool> IsReport(Post existing, string moderators, string reporterId, string postId, CancellationToken ct)
    {
        if (existing.TangentId != moderators || existing.AuthorId != reporterId) return false;
        var root = existing.ParentId is null ? existing : await Post.Get(existing.TopicId, ct);
        return root?.OriginId == postId;
    }

    /// <summary>The reporter's earlier report of this Post that still stands — a root of the moderators' Tangent, neither
    /// removed nor locked — or null: one read of <c>ix_post_origins</c>'s range for the Post, which holds the Topics
    /// taken from it and nothing else.</summary>
    private async Task<Post?> Standing(string moderators, CancellationToken ct)
    {
        var (postId, reporterId) = (PostId, ReporterId);
        var found = await Post.Query(post => post.OriginId == postId && post.TangentId == moderators && post.AuthorId == reporterId
                                             && post.ParentId == null && post.Removed == false && post.Locked == false,
            new QueryDefinition { Page = 1, PageSize = 1 }, ct);
        return found.Count > 0 ? found[0] : null;
    }
}
