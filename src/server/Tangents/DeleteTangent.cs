using System.Globalization;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>Deletes a Tangent whole, in one hold: its Posts with their snapshots and their media uses, its asks, every
/// overwrite on its resource (<see cref="Overwrites.RemoveAll"/>), its image's use and its row, and appends
/// <see cref="EventKind.TangentDeleted"/>; nothing of it stays in its place. Its rows of the log stay, and reach no one
/// once it has no row (<see cref="Capabilities.LoadEach"/>); a Connection takes down each copy of its articles from the
/// copy's receipt (<see cref="Connections.Syndication"/>). The Tangent exists for the actor, who holds
/// <see cref="Bit.View"/> on it, or the command answers that no Tangent has the slug, as sending it again does; the actor
/// holds <see cref="Bit.ManageSpace"/> in the place, or is refused with the gate word; the moderators' Tangent stays
/// (<see cref="Kept"/>); and <see cref="Topics"/> says how many Topics go with it, as many as it holds, or the command is
/// refused (<see cref="HoldsTopics"/>), so a bare send deletes only an empty Tangent. A Topic, once started, stays counted
/// (<see cref="Tangent.TopicCount"/>), raised in the commit that starts it, and holds take turns, so a Topic started after
/// the count was read refuses the delete. The Posts go a page of <see cref="PostPage"/> at a time, each read finding what
/// the last left, since a command reads its own deletes; a Tangent too large for one hold fails whole, and nothing
/// changes. The order keeps its gap.</summary>
public sealed record DeleteTangent(string Slug, string ActorId, int Topics = 0, DateTimeOffset? At = null) : ICommand<Tangent>
{
    /// <summary>The answer to a delete of the moderators' Tangent.</summary>
    public static readonly Reason Kept = Reason.Invalid("The moderators’ Tangent stays: every report goes there.");

    /// <summary>The word of a delete whose count of Topics is not the Tangent's.</summary>
    public const string HoldsTopicsWord = "holds-topics";

    /// <summary>The most Posts one read of the delete takes.</summary>
    public const int PostPage = 500;

    /// <summary>The most asks one read of the delete takes.</summary>
    public const int AskPage = 100;

    /// <summary>The answer to a delete that names another count of Topics than the <paramref name="count"/> the Tangent
    /// holds: what goes with it, read again before it is sent again.</summary>
    public static Reason HoldsTopics(int count) => Reason.Conflict(HoldsTopicsWord, count switch
    {
        0 => "It holds no topics now: read what goes with it, then delete it again.",
        1 => "It holds one topic now, which would go with it: read what goes with it, then delete it again.",
        _ => $"It holds {count.ToString(CultureInfo.InvariantCulture)} topics now, which would go with it: read what goes with it, then delete it again."
    });

    public async Task<Outcome<Tangent>> Run(CancellationToken ct)
    {
        if (await Tangent.Get(Slug, ct) is not { } tangent || !(await Capabilities.Load(ActorId, Slug, null, ct)).Exists)
            return new Outcome<Tangent>.Refused(ChangeTangent.NoTangent);
        if ((await Capabilities.Load(ActorId, null, null, ct)).Gate(Bit.ManageSpace) is { } gate) return new Outcome<Tangent>.Refused(gate);
        if ((await TangentSpace.Get(TangentSpace.SingleRow, ct))?.ModeratorsTangentId == Slug) return new Outcome<Tangent>.Refused(Kept);
        if (tangent.TopicCount != Topics) return new Outcome<Tangent>.Refused(HoldsTopics(tangent.TopicCount));

        var read = PostPage;
        while (read == PostPage)
        {
            var posts = await Post.Query(post => post.TangentId == Slug, new QueryDefinition { Page = 1, PageSize = PostPage }, ct);
            read = posts.Count;
            if (read == 0) break;
            await Post.Remove(posts.SelectMany(post => Versions(post).Select(version => AuditPartition.Key(post.Id, version))), AuditPartition.Name, ct);
            await MediaUse.Remove(posts.SelectMany(post => post.UsedMedia().Select(media => MediaUse.Key(media, post.Id))), ct);
            await Post.Remove(posts.Select(post => post.Id), ct);
        }

        read = AskPage;
        while (read == AskPage)
        {
            var asks = await Ask.Query(ask => ask.TangentId == Slug, new QueryDefinition { Page = 1, PageSize = AskPage }, ct);
            read = asks.Count;
            if (read > 0) await Ask.Remove(asks.Select(ask => ask.Id), ct);
        }

        await Overwrites.RemoveAll(AppHost.GetRequiredService<RoleCollection>(nameof(DeleteTangent)), Slug, ct);
        await MediaUse.Record(Slug, tangent.Image, "", ActorId, ct);
        await tangent.Remove(ct);
        await Event.Append(EventKind.TangentDeleted, At ?? DateTimeOffset.UtcNow, ct, tangentId: Slug, actorId: ActorId);
        return new Outcome<Tangent>.Done(tangent);
    }

    /// <summary>Every version a snapshot of <paramref name="post"/> may be kept at: an edit or a removal snapshots the
    /// version it changes (<see cref="AuditPartition"/>).</summary>
    private static IEnumerable<long> Versions(Post post)
    {
        for (var version = 1L; version <= post.Version; version++) yield return version;
    }
}
