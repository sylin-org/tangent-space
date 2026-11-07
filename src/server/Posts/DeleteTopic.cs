using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Events;
using Tangent.Space;

namespace Tangent.Posts;

/// <summary>Deletes a Topic whole, in one hold: its root and every reply, with their snapshots and their media uses
/// (<see cref="Rows"/>), so no tombstone stays; its Tangent counts it no longer (<see cref="Tangents.Tangent.TopicWent"/>) and
/// pins it no more; and <see cref="EventKind.TopicDeleted"/> is appended, targeting no one. Its rows of the log stay, and every
/// reader drops them, since each needs the Post; a Connection takes down an article's copy from its receipt alone
/// (<see cref="Connections.Syndication"/>). The Topic exists for the actor, who holds <see cref="Bit.View"/> in its Tangent, or
/// the command answers that no Topic has the Id, as sending it again does; the actor holds <see cref="Bit.Moderate"/> in the
/// Tangent, asked of the Tangent so a removed root is deleted too, or is refused with the gate word; a report stays
/// (<see cref="ReportStays"/>); and <see cref="Replies"/> says how many replies go with it, as many as it holds, or the command
/// is refused (<see cref="HoldsReplies"/>), so a reply that landed after its page was read refuses the delete: a Topic's count
/// of replies only rises, and holds take turns. The Posts go a page of <see cref="Tangents.DeleteTangent.PostPage"/> at a
/// time, each read finding what the last left; a Topic too large for one hold fails whole, and nothing changes.</summary>
public sealed record DeleteTopic(string TopicId, string ActorId, int Replies = 0, DateTimeOffset? At = null) : ICommand<Post>
{
    /// <summary>The word of a delete whose count of replies is not the Topic's.</summary>
    public const string HoldsRepliesWord = "holds-replies";

    /// <summary>The answer to a delete of a report in the moderators' Tangent, which keeps every report.</summary>
    public static readonly Reason ReportStays = Reason.Invalid("A report stays in the moderators’ Tangent: remove its words instead.");

    /// <summary>The answer to a delete that names another count of replies than the <paramref name="count"/> the Topic holds:
    /// what goes with it, read again before it is sent again.</summary>
    public static Reason HoldsReplies(int count) => Reason.Conflict(HoldsRepliesWord, count switch
    {
        0 => "It holds no replies now: read what goes with it, then delete it again.",
        1 => "It holds one reply now, which would go with it: read what goes with it, then delete it again.",
        _ => $"It holds {count.ToString(CultureInfo.InvariantCulture)} replies now, which would go with it: read what goes with it, then delete it again."
    });

    public async Task<Outcome<Post>> Run(CancellationToken ct)
    {
        if (await Post.Get(TopicId, ct) is not { ParentId: null } root) return new Outcome<Post>.Refused(Reply.NoTopic);
        var around = await Capabilities.Load(ActorId, root.TangentId, null, ct);
        if (!around.Exists || await Tangents.Tangent.Get(root.TangentId, ct) is not { } tangent) return new Outcome<Post>.Refused(Reply.NoTopic);
        if (around.Gate(Bit.Moderate) is { } gate) return new Outcome<Post>.Refused(gate);
        if ((await TangentSpace.Get(TangentSpace.SingleRow, ct))?.ModeratorsTangentId == root.TangentId) return new Outcome<Post>.Refused(ReportStays);
        if (root.ReplyCount != Replies) return new Outcome<Post>.Refused(HoldsReplies(root.ReplyCount));

        var read = Tangents.DeleteTangent.PostPage;
        while (read == Tangents.DeleteTangent.PostPage)
        {
            var posts = await Post.Query(post => post.TopicId == root.Id, new QueryDefinition { Page = 1, PageSize = Tangents.DeleteTangent.PostPage }, ct);
            read = posts.Count;
            if (read > 0) await Rows(posts, ct);
        }

        tangent.TopicWent(root);
        tangent.Unpin(root.Id);
        await tangent.Save(ct);
        await Event.Append(EventKind.TopicDeleted, At ?? DateTimeOffset.UtcNow, ct,
            tangentId: root.TangentId, topicId: root.Id, postId: root.Id, actorId: ActorId);
        return new Outcome<Post>.Done(root);
    }

    /// <summary>Inside the running command, deletes <paramref name="posts"/> and what each holds: every snapshot of it, its
    /// media uses, and its row.</summary>
    internal static async Task Rows(IReadOnlyList<Post> posts, CancellationToken ct)
    {
        await Post.Remove(posts.SelectMany(post => Versions(post).Select(version => AuditPartition.Key(post.Id, version))), AuditPartition.Name, ct);
        await MediaUse.Remove(posts.SelectMany(post => post.UsedMedia().Select(media => MediaUse.Key(media, post.Id))), ct);
        await Post.Remove(posts.Select(post => post.Id), ct);
    }

    /// <summary>Every version a snapshot of <paramref name="post"/> may be kept at: an edit or a removal snapshots the
    /// version it changes (<see cref="AuditPartition"/>).</summary>
    private static IEnumerable<long> Versions(Post post)
    {
        for (var version = 1L; version <= post.Version; version++) yield return version;
    }
}
