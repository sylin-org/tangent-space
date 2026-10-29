using Koan.Data.Abstractions;
using Koan.Data.Core.Sorting;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>Take this on a tangent's reads, both ways: where a reader may take a Post (<see cref="Destinations"/>), where
/// a Topic was taken from (<see cref="Of"/>), and the Topics taken from each of a page's Posts (<see cref="From"/>).
/// Each answers only what the reader may view: an origin in a Tangent absent to them is a Post they cannot see, with
/// nothing of it, and a Topic in such a Tangent is not among those taken from a Post. Every read is bounded and keyed and
/// takes no lock; a Tangent's row is read before its overwrites.</summary>
public static class Origins
{
    /// <summary>The most Tangents read, by order, for the Tangents a reader may start a Topic in.</summary>
    public const int TangentLimit = TangentPageController.SwitcherLimit;

    /// <summary>The most Topics taken from one Post that a page shows; past it, the page says "at least".</summary>
    public const int TakenLimit = 5;

    /// <summary>The most Topics taken from one Post that are read, newest first, through <c>ix_post_origins</c>, so a
    /// few in Tangents the reader may not view leave room for those they may.</summary>
    public const int TakenRead = 12;

    /// <summary>The Tangents among the first <see cref="TangentLimit"/> by order where the reader holds
    /// <see cref="Bit.Topic"/>, in order: one read of the Tangents and one of their overwrites from Koan's cache. None for a
    /// visitor, who starts a Topic nowhere.</summary>
    public static async Task<IReadOnlyList<Tangents.Tangent>> Destinations(string? personId, CancellationToken ct)
    {
        if (personId is null) return [];
        var rows = await Tangents.Tangent.All(new QueryDefinition { Page = 1, PageSize = TangentLimit }
            .WithSort<Tangents.Tangent>(sort => sort.OrderBy(tangent => tangent.Order).ThenBy(tangent => tangent.Id)), ct);
        var each = await Capabilities.LoadEach(personId, rows.Select(row => row.Id).ToList(), ct);
        return rows.Where(row => each[row.Id].Holds(Bit.Topic)).ToList();
    }

    /// <summary>Where <paramref name="root"/> was taken on a tangent from, as the reader may know it; null for a Topic
    /// started on its own. The origin, its Tangent's row, capability's rows there, its Topic's root and its author, each
    /// one keyed read. A reader for whom the origin's Tangent is absent — or an origin no row holds — reads only that it
    /// was taken from a Post they cannot see. A removed origin is its tombstone's address.</summary>
    public static async Task<TopicPage.Origin?> Of(Post root, string? personId, string configured, CancellationToken ct)
    {
        if (root.OriginId is not { } originId) return null;
        if (await Post.Get(originId, ct) is not { } origin || await Tangents.Tangent.Get(origin.TangentId, ct) is not { } tangent)
            return TopicPage.Origin.Unseen;
        var rows = await Capabilities.LoadRows(personId, tangent.Id, ct);
        if (!rows.For(null).Exists) return TopicPage.Origin.Unseen;
        var topic = origin.ParentId is null ? origin : await Post.Get(origin.TopicId, ct);
        if (topic is null) return TopicPage.Origin.Unseen;
        var author = await Participant.Get(origin.AuthorId, ct);
        var handle = author?.Handle is { Length: > 0 } stored ? stored : origin.AuthorId;
        return new TopicPage.Origin(
            false,
            PostWrites.Address(configured, tangent.Id, topic.Slug ?? topic.Id, origin.Id),
            topic.Title ?? "",
            tangent.Name,
            author?.Profile.DisplayName is { Length: > 0 } name ? name : handle,
            origin.Removed);
    }

    /// <summary>The Topics taken on a tangent from each of <paramref name="origins"/> that the reader may view, by origin
    /// Id, newest first, at most <see cref="TakenLimit"/> each, with how many of those read the reader may view, and
    /// "at least" where more than the bound are or the read was full, since more they may view can lie past it; an
    /// origin with none is not in the answer. One range of <c>ix_post_origins</c> per origin, at most <see cref="TakenRead"/> rows, leaving
    /// out the reports in a moderators' Tangent the reader may not view (<see cref="HiddenReports"/>), then the Tangents of
    /// what was found by Id and their overwrites from Koan's cache.</summary>
    public static async Task<IReadOnlyDictionary<string, TopicPage.Taken>> From(IReadOnlyList<Post> origins, string? personId, string configured,
        CancellationToken ct)
    {
        var found = new Dictionary<string, IReadOnlyList<Post>>(StringComparer.Ordinal);
        var hidden = await HiddenReports(personId, ct);
        foreach (var origin in origins.DistinctBy(post => post.Id))
        {
            var originId = origin.Id;
            var query = new QueryDefinition { Page = 1, PageSize = TakenRead }.WithSort<Post>(sort => sort.OrderByDescending(post => post.CreatedAt));
            var rows = hidden is null
                ? await Post.Query(post => post.OriginId == originId, query, ct)
                : await Post.Query(post => post.OriginId == originId && post.TangentId != hidden, query, ct);
            if (rows.Count > 0) found[originId] = rows;
        }

        if (found.Count == 0) return new Dictionary<string, TopicPage.Taken>();
        var tangentIds = found.Values.SelectMany(rows => rows).Select(post => post.TangentId).Distinct(StringComparer.Ordinal).ToList();
        var tangents = (await Tangents.Tangent.Get(tangentIds, ct)).OfType<Tangents.Tangent>().ToDictionary(row => row.Id, StringComparer.Ordinal);
        var each = await Capabilities.LoadEach(personId, tangents.Keys.ToList(), ct);
        var taken = new Dictionary<string, TopicPage.Taken>(StringComparer.Ordinal);
        foreach (var (originId, rows) in found)
        {
            var viewable = rows.Where(post => tangents.ContainsKey(post.TangentId) && each[post.TangentId].Exists).ToList();
            if (viewable.Count == 0) continue;
            taken[originId] = new TopicPage.Taken(
                viewable.Take(TakenLimit).Select(post => new TopicPage.TakenTopic(
                    post.Title ?? "",
                    PostWrites.TopicAddress(configured, post.TangentId, post.Slug ?? post.Id),
                    tangents[post.TangentId].Name,
                    post.Removed)).ToList(),
                viewable.Count,
                viewable.Count > TakenLimit || rows.Count == TakenRead);
        }

        return taken;
    }

    /// <summary>The moderators' Tangent where the reader may not view it, so the Topics a report starts there are not
    /// read at all: they never crowd out those the reader may view or make a page say "at least"; null where
    /// the reader may view it or the place records none. The settings row and the Tangent's overwrites from Koan's cache.</summary>
    private static async Task<string?> HiddenReports(string? personId, CancellationToken ct)
    {
        if ((await TangentSpace.Get(TangentSpace.SingleRow, ct))?.ModeratorsTangentId is not { } moderators) return null;
        return (await Capabilities.LoadEach(personId, [moderators], ct))[moderators].Exists ? null : moderators;
    }
}
