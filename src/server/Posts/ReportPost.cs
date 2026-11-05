using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Events;
using Tangent.Space;

namespace Tangent.Posts;

/// <summary>Reports a Post: takes it on a tangent into the moderators' Tangent (<see cref="ModeratorsTangent"/>) as a
/// Topic whose root carries the reported Post as its origin, the reporter as its author, a title (<see cref="Title"/>)
/// and as its body <see cref="Opening"/>, the reason and what was reported (<see cref="OpeningOf"/>), or the reason alone,
/// which may be none. The title names the reported Topic, and the opening quotes the reported words, only where the
/// moderator role may view the reported Tangent (<see cref="ModeratorsTangent.MayView"/>); elsewhere the title is the
/// reason's first words as sent, or <see cref="Unsaid"/>, and the opening says who wrote it and in which Tangent, so no
/// words of a Tangent the moderators may not view reach theirs. Reporting is its own verb, held by every participant for
/// whom the Post exists, whatever they hold in the moderators' Tangent: the Post exists for the reporter, who holds
/// <see cref="Bit.View"/> where it is, or the command answers that no Post has the Id before anything else; a visitor is
/// refused with <see cref="Capabilities.SignIn"/>; a Post of the moderators' Tangent with <see cref="InModerators"/>; the
/// reporter's own Post with <see cref="Own"/>; a removed Post with <see cref="Capabilities.Removed"/>. The <see cref="EventKind.PostCreated"/> it appends targets
/// nobody, so the reported author hears nothing of it, and is marked a report (<see cref="Event.Report"/>), which is how it
/// reaches everyone who may view the moderators' Tangent among what is for them on their door, without one row written
/// for each of them (<see cref="Unread"/>); and one <see cref="EventKind.Mention"/> goes to each participant
/// the reason mentions who may view the moderators' Tangent (<see cref="Mentions.Notify"/>). A participant who reports
/// the same Post again while their earlier report stands — its root not removed — replies in that
/// report's Topic with the new reason, so the moderators keep one Topic per reporter and Post with every reason in
/// order, and with no reason answers with that report and writes nothing; once the moderators remove it, the next
/// report starts a Topic of its own. Idempotent by Id: the same
/// Id from the same reporter about the same Post answers with the Post it wrote, whatever reason is sent and whatever
/// the moderators have done with it since, so a resend says nothing of the report's state; any other Post holding it is
/// refused with <see cref="Post.IdTaken"/>, which says nothing of that Post. The reason is judged where the command is built
/// (<see cref="PostBody"/>), never in its hold.</summary>
public sealed record ReportPost(
    string Id,
    string PostId,
    PostBody Reason,
    string ReporterId,
    DateTimeOffset? At = null,
    PostBody? Opening = null) : ICommand<Post>
{
    public static readonly Reason NoPost = Koan.Data.Core.Commands.Reason.Absent("No Post has this Id.");

    /// <summary>What a report's title names where it names no Topic and its reporter said nothing.</summary>
    public const string Unsaid = "a post";

    /// <summary>The answer where the place records no moderators' Tangent, which a start creates.</summary>
    public static readonly Reason NoModerators = Koan.Data.Core.Commands.Reason.Invalid("This Tangent Space has no moderators' Tangent yet, so the report was not sent.");

    /// <summary>The answer for a Post of the moderators' Tangent, which is already where the moderators read.</summary>
    public static readonly Reason InModerators = Koan.Data.Core.Commands.Reason.Invalid("Posts in the moderators' Tangent are not reported; the moderators already read them there.");

    /// <summary>The answer for the reporter's own Post, which they edit or remove themselves.</summary>
    public static readonly Reason Own = Koan.Data.Core.Commands.Reason.Invalid("You report someone else’s Post; your own you can edit or remove.");

    /// <summary>What a report's title starts with, before the reported Topic's title or the reason's first words.</summary>
    public const string TitlePrefix = "Report: ";

    /// <summary>The most characters of the reason a title carries where it names no Topic.</summary>
    public const int ReasonWords = 60;

    public async Task<Outcome<Post>> Run(CancellationToken ct)
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
        if (reported.AuthorId == ReporterId) return new Outcome<Post>.Refused(Own);

        if (reported.Removed) return new Outcome<Post>.Refused(Capabilities.Removed);
        var at = At ?? DateTimeOffset.UtcNow;
        var earlier = await Standing(moderators, ct);
        var said = !string.IsNullOrWhiteSpace(Reason.Text);
        if (earlier is not null && !said) return new Outcome<Post>.Done(earlier);
        Post written;
        if (earlier is not null)
        {
            written = earlier.Reply(Id, Reason, ReporterId, earlier.Id, at);
        }
        else
        {
            var title = Title(await ModeratorsTangent.MayView(reported.TangentId, rows.Space, ct) ? topic.Title ?? "" : said ? FirstWords(Reason.Sent) : Unsaid);
            written = Post.Root(Id, into.Id, title, await StartTopic.UniqueSlug(into.Id, title, Id, ct), into.DefaultKind, Opening ?? Reason, ReporterId, at,
                reported.Id);
            into.TopicStarted();
            await into.Save(ct);
        }

        var root = earlier ?? written;
        var created = await Event.Append(EventKind.PostCreated, at, ct,
            tangentId: written.TangentId, topicId: written.TopicId, postId: written.Id, actorId: ReporterId, version: written.Version,
            report: true);
        written.Logged(created.Seq, root);
        await written.Save(ct);
        if (earlier is not null) await earlier.Save(ct);
        await Seen.Wrote(ReporterId, root, written.Sequence, ct);
        await MediaUse.Record(written, ReporterId, [], ct);
        await Mentions.Notify(written, ReporterId, [], at, ct);
        return new Outcome<Post>.Done(written);
    }

    /// <summary>What a report's Topic opens with: the reason as sent, then what was reported as the moderators may know it.
    /// Where the moderator role may view the reported Tangent (<see cref="ModeratorsTangent.MayView"/>), the reported Post's
    /// opening words, quoted and linked to it; elsewhere who wrote it, linked to their profile, and the Tangent's name, never
    /// its words. Built where the command is built, as the reason is, and written as a body is
    /// (<see cref="PostBody.Written"/>), which reads none of what was reported for mentions, every word of it inside a
    /// link; null where the whole would not be written, or there are no words to quote, when the report opens with the
    /// reason alone. The reported Post's author and whoever it mentions in one keyed read, and what
    /// <see cref="ModeratorsTangent.MayView"/> reads.</summary>
    public static async Task<PostBody?> OpeningOf(string? reason, Post reported, Post topic, Tangents.Tangent tangent, TangentSpace space,
        string baseAddress, CancellationToken ct)
    {
        var people = await QuotedPost.People(reported, ct);
        string what;
        if (await ModeratorsTangent.MayView(tangent.Id, space, ct))
        {
            var words = Tangents.TangentPage.Opening(reported, people);
            if (words.Length == 0) return null;
            what = "> [" + Escaped(words) + "](" + PostWrites.Address(baseAddress, tangent.Id, topic.Slug ?? topic.Id, reported.Id) + ")";
        }
        else
        {
            var handle = people.TryGetValue(reported.AuthorId, out var author) ? author.Handle : reported.AuthorId;
            what = "*A post by [" + Escaped("@" + handle) + "](" + Participants.ProfilePage.AddressOf(baseAddress, handle) + ") in "
                   + Escaped(tangent.Name) + ", which moderators can’t read.*";
        }

        var body = await PostBody.Written(string.IsNullOrWhiteSpace(reason) ? what : reason.TrimEnd() + "\n\n" + what, ct);
        return body.Refusal is null ? body : null;
    }

    /// <summary>Plain words as Markdown that reads them as they are: every ASCII punctuation mark escaped.</summary>
    private static string Escaped(string words)
        => string.Concat(words.Select(c => char.IsAscii(c) && char.IsPunctuation(c) || char.IsAscii(c) && char.IsSymbol(c) ? "\\" + c : c.ToString()));

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

    /// <summary>The reporter's earlier report of this Post that still stands — a root of the moderators' Tangent that is
    /// not removed — or null: one read of <c>ix_post_origins</c>'s range for the Post, which holds the Topics
    /// taken from it and nothing else.</summary>
    private async Task<Post?> Standing(string moderators, CancellationToken ct)
    {
        var (postId, reporterId) = (PostId, ReporterId);
        var found = await Post.Query(post => post.OriginId == postId && post.TangentId == moderators && post.AuthorId == reporterId
                                             && post.ParentId == null && post.Removed == false,
            new QueryDefinition { Page = 1, PageSize = 1 }, ct);
        return found.Count > 0 ? found[0] : null;
    }
}
