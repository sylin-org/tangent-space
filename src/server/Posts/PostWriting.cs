using Koan.Data.Core;
using Tangent.Events;

namespace Tangent.Posts;

/// <summary>What every write that brings a Post does once the Post is filled (<see cref="Post.StartIn"/>,
/// <see cref="Post.ReplyTo"/>), in the same hold, whether it starts a Topic, replies or reports (<see cref="StartTopic"/>,
/// <see cref="Reply"/>, <see cref="ReportPost"/>).</summary>
public static class PostWriting
{
    /// <summary>Writes <paramref name="written"/>, a Post of the Topic whose root is <paramref name="root"/>, the root itself
    /// for a Topic that starts, by its author: a Topic that starts is counted in <paramref name="tangent"/>, in all, in its
    /// month and by its author (<see cref="Tangents.Tangent.TopicCame"/>); <see cref="EventKind.PostCreated"/> is appended at
    /// <paramref name="at"/>, targeting <paramref name="targetId"/> — the author of what it answers, none for their own — and
    /// marked a report where <paramref name="report"/> says so (<see cref="Event.Report"/>); the Post and its root carry that
    /// row's sequence (<see cref="Post.Logged"/>) and are saved, the root's counters with it; what the Post uses is recorded
    /// (<see cref="MediaUse.Record(Post, string, IReadOnlyList{string}, CancellationToken)"/>); the author's mark on the Topic
    /// moves onto the Post where it stood just before (<see cref="Seen.Wrote"/>) and, unless they report, they follow the
    /// Topic unless they stopped (<see cref="Follow.Wrote"/>); a reply is new to the Topic's other followers
    /// (<see cref="Follow.Moved"/>); and one <see cref="EventKind.Mention"/> goes to each participant and role the body
    /// mentions (<see cref="Mentions.Notify"/>).</summary>
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
        await written.Save(ct);
        if (written.ParentId is not null) await root.Save(ct);
        await MediaUse.Record(written, author, [], ct);
        var caughtUp = await Seen.Wrote(author, root, written.Sequence, ct);
        if (!report) await Follow.Wrote(author, root, caughtUp, ct);
        if (written.ParentId is not null) await Follow.Moved(root, author, ct);
        await Mentions.Notify(written, author, [], at, ct);
    }
}
