using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Events;
using Tangent.Space;

namespace Tangent.Posts;

/// <summary>Moves a Topic to another Tangent, its replies with it, in one hold. Its root keeps its kind, and its slug where
/// the Tangent it moves to holds none of it, else takes the first numbered suffix there (<see cref="StartTopic.UniqueSlug"/>);
/// its address where it stood becomes a <see cref="FormerAddress"/>, which forwards. Every Post of it, each Post's media uses
/// and every row of the log about it, one range of <c>ix_event_topics</c>, take the new Tangent a page of
/// <see cref="Tangents.DeleteTangent.PostPage"/> at a time, each read finding what the last left, so every reader of the log
/// stays right and none changes; each row also takes where the new Tangent's own rows begin (<see cref="Event.Into"/>), so a
/// row older than the new Tangent itself is still its own and reaches its readers (<see cref="Capabilities.Owns"/>). The
/// Tangent it leaves counts it no longer and pins it no more, the one it enters counts
/// it (<see cref="Tangents.Tangent.TopicWent"/>, <see cref="Tangents.Tangent.TopicCame"/>), and
/// <see cref="EventKind.PostMoved"/> is appended last, in the new Tangent, targeting no one, which wakes the Connections, so an
/// article's copy follows it. The Topic exists for the actor, who holds <see cref="Bit.View"/> in its Tangent, or the command
/// answers that no Topic has the Id; the actor holds <see cref="Bit.Moderate"/> there, asked of the Tangent so a removed root
/// moves too, or is refused with the gate word; a move to where it stands answers it as it is and writes nothing; the
/// moderators' Tangent neither gives nor takes a Topic (<see cref="KeepsReports"/>), so a reporter is never exposed; the
/// Tangent it moves to exists for the actor (<see cref="NoDestination"/>), who holds <see cref="Bit.Moderate"/> there too.
/// A Topic too large for one hold fails whole, and nothing changes.</summary>
public sealed record MoveTopic(string TopicId, string To, string ActorId, DateTimeOffset? At = null) : ICommand<Post>
{
    /// <summary>The field that names the Tangent a Topic moves to.</summary>
    public const string ToField = "to";

    /// <summary>The most rows of the log one read of the move takes.</summary>
    public const int EventPage = 500;

    public static readonly Reason KeepsReports = Reason.Invalid("The moderators’ Tangent keeps its reports and takes no other topic.");

    public static readonly Reason NoDestination = Reason.Invalid(ToField, "No Tangent you may read has this slug.");

    public async Task<Outcome<Post>> Run(CancellationToken ct)
    {
        if (await Post.Get(TopicId, ct) is not { ParentId: null } root) return new Outcome<Post>.Refused(Reply.NoTopic);
        var from = root.TangentId;
        var here = await Capabilities.Load(ActorId, from, null, ct);
        if (!here.Exists || await Tangents.Tangent.Get(from, ct) is not { } source) return new Outcome<Post>.Refused(Reply.NoTopic);
        if (here.Gate(Bit.Moderate) is { } gate) return new Outcome<Post>.Refused(gate);
        if (To == from) return new Outcome<Post>.Done(root);
        var moderators = (await TangentSpace.Get(TangentSpace.SingleRow, ct))?.ModeratorsTangentId;
        if (from == moderators || To == moderators) return new Outcome<Post>.Refused(KeepsReports);
        if (!Slug.IsValid(To) || await Tangents.Tangent.Get(To, ct) is not { } target) return new Outcome<Post>.Refused(NoDestination);
        var there = await Capabilities.Load(ActorId, To, null, ct);
        if (!there.Exists) return new Outcome<Post>.Refused(NoDestination);
        if (there.Gate(Bit.Moderate) is { } targetGate) return new Outcome<Post>.Refused(targetGate);

        var formerSlug = root.Slug ?? root.Id;
        await new FormerAddress { Id = FormerAddress.Key(from, formerSlug), TopicId = root.Id }.Save(ct);
        root.Slug = await StartTopic.UniqueSlug(To, formerSlug, root.Id, ct);
        root.TangentId = To;
        await root.Save(ct);
        await Uses([root], ct);

        var read = Tangents.DeleteTangent.PostPage;
        while (read == Tangents.DeleteTangent.PostPage)
        {
            var posts = await Post.Query(post => post.TopicId == root.Id && post.TangentId == from,
                new QueryDefinition { Page = 1, PageSize = Tangents.DeleteTangent.PostPage }, ct);
            read = posts.Count;
            if (read == 0) break;
            foreach (var post in posts) post.TangentId = To;
            await Post.UpsertMany(posts, ct);
            await Uses(posts, ct);
        }

        read = EventPage;
        while (read == EventPage)
        {
            var rows = await Event.Query(row => row.TopicId == root.Id && row.TangentId == from, new QueryDefinition { Page = 1, PageSize = EventPage }, ct);
            read = rows.Count;
            if (read == 0) break;
            foreach (var row in rows) (row.TangentId, row.Into) = (To, target.LogSeq);
            await Event.UpsertMany(rows, ct);
        }

        source.TopicWent(root);
        source.Unpin(root.Id);
        await source.Save(ct);
        target.TopicCame(root);
        await target.Save(ct);
        await Event.Append(EventKind.PostMoved, At ?? DateTimeOffset.UtcNow, ct,
            tangentId: To, topicId: root.Id, postId: root.Id, actorId: ActorId, version: root.Version);
        return new Outcome<Post>.Done(root);
    }

    /// <summary>Inside the running command, takes the media uses of <paramref name="posts"/>, which stand in the new Tangent,
    /// there too: each read by its key.</summary>
    private async Task Uses(IReadOnlyList<Post> posts, CancellationToken ct)
    {
        var keys = posts.SelectMany(post => post.UsedMedia().Select(media => MediaUse.Key(media, post.Id))).ToList();
        if (keys.Count == 0) return;
        var uses = (await MediaUse.Get(keys, ct)).OfType<MediaUse>().ToList();
        foreach (var use in uses) use.TangentId = To;
        if (uses.Count > 0) await MediaUse.UpsertMany(uses, ct);
    }
}
