using System.Globalization;
using System.Net;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Sorting;
using Tangent.Participants;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>What a feed reads: a bounded window of the newest Topics a visitor may read, of one Tangent or the place's
/// articles, newest first and none removed, each root's body rendered as its page renders it. A feed always reads as a
/// visitor, whoever asks, so it says the same to everyone and carries nothing a visitor could not read; deciding that a
/// visitor may read it at all is the caller's. An image in a body and an article's cover are at their addresses under the
/// base URL, which a reader with no account fetches as a visitor. Reads only and takes no lock: a Tangent's, at most
/// <see cref="Feed.Limit"/> Posts in one query through <c>ix_post_started</c>; the articles', <see cref="Feed.Limit"/> at a
/// time through <c>ix_post_articles</c>, at most <see cref="ViewableTangents.Met.ReadLimit"/> reads, until the window is full
/// of those a visitor may view (<see cref="OfArticles"/>); and everyone they name by key.</summary>
public static class Feeds
{
    /// <summary>A Tangent's feed under its address, without an extension.</summary>
    public const string TangentSegment = "feed";

    /// <summary>The place's articles under the base URL, without an extension.</summary>
    public const string ArticlesPath = "/articles";

    /// <summary>Where a Tangent's feed is, without an extension.</summary>
    public static string TangentAddress(string baseUrl, string slug) => baseUrl + "/t/" + Uri.EscapeDataString(slug) + "/" + TangentSegment;

    /// <summary>Where any Tangent's feed is, without an extension, its slug standing in as <c>{tangent}</c>.</summary>
    public static string AnyTangentAddress(string baseUrl) => baseUrl + "/t/{tangent}/" + TangentSegment;

    /// <summary>Where the place's articles feed is, without an extension.</summary>
    public static string ArticlesAddress(string baseUrl) => baseUrl + ArticlesPath;

    /// <summary>A Tangent's two feeds, as its pages advertise them.</summary>
    public static IReadOnlyList<FeedLink> TangentLinks(string baseUrl, string slug, string name, string place)
        => FeedLink.Pair(TangentTitle(name, place), TangentAddress(baseUrl, slug));

    /// <summary>The place's two articles feeds, as its door advertises them.</summary>
    public static IReadOnlyList<FeedLink> ArticlesLinks(string baseUrl, string place)
        => FeedLink.Pair(ArticlesTitle(place), ArticlesAddress(baseUrl));

    /// <summary>The feed of a Tangent a visitor may read.</summary>
    public static async Task<Feed> OfTangent(Tangents.Tangent tangent, TangentSpace space, string baseUrl, CancellationToken ct)
    {
        var address = baseUrl + "/t/" + Uri.EscapeDataString(tangent.Id);
        var query = Newest();
        var topics = await Post.Query(post => post.TangentId == tangent.Id && post.ParentId == null && !post.Removed, query, ct);
        return new Feed(TangentTitle(tangent.Name, space.Name), tangent.Description, address, TangentAddress(baseUrl, tangent.Id),
            await Entries(topics, baseUrl, ct));
    }

    /// <summary>The feed of the place's newest articles a visitor may view: read newest first, each read continuing by seek
    /// from the last article the one before read, and kept only in the Tangents a visitor may view, each judged once as a
    /// read names it (<see cref="ViewableTangents.Met"/>), so a members-only Tangent's articles never show and every Tangent no
    /// article names is never read. Each read after the first leaves out the Tangents met so far that a visitor may not view,
    /// so it fills the window or meets a Tangent no earlier read met. The reading stops once the window is full, or after
    /// <see cref="ViewableTangents.Met.ReadLimit"/> reads, and the window holds what it found.</summary>
    public static async Task<Feed> OfArticles(TangentSpace space, string baseUrl, CancellationToken ct)
    {
        var met = new ViewableTangents.Met(null);
        var kept = new List<Post>();
        Filter? after = null;
        for (var reads = 0; reads < ViewableTangents.Met.ReadLimit && kept.Count < Feed.Limit; reads++)
        {
            var rows = await Post.Query(post => post.Kind == PostKind.Article && post.ParentId == null && !post.Removed,
                Newest() with { Filter = Filter.And(after, met.LeavingOut(nameof(Post.TangentId))) }, ct);
            kept.AddRange((await met.Keep(rows, post => post.TangentId, ct)).Take(Feed.Limit - kept.Count));
            if (rows.Count < Feed.Limit) break;
            after = Before(rows[^1]);
        }

        return new Feed(ArticlesTitle(space.Name), space.Description, baseUrl + "/", ArticlesAddress(baseUrl), await Entries(kept, baseUrl, ct));
    }

    /// <summary>The Posts after <paramref name="last"/> in the order <see cref="Newest"/> reads them: started no later, which
    /// the index answers as a range, and, at its own instant, with an Id before its Id.</summary>
    private static Filter Before(Post last)
    {
        var at = FieldPath.Of(nameof(Post.CreatedAt));
        var id = FieldPath.Of(nameof(Post.Id));
        return Filter.All(
            Filter.On(at, FilterOperator.Lte, FilterValue.Of(last.CreatedAt)),
            Filter.Any(
                Filter.On(at, FilterOperator.Lt, FilterValue.Of(last.CreatedAt)),
                Filter.On(id, FilterOperator.Lt, FilterValue.Of(last.Id))));
    }

    private static string TangentTitle(string name, string place) => name + " · " + place;

    private static string ArticlesTitle(string place) => "Articles · " + place;

    /// <summary>The first <see cref="Feed.Limit"/> Posts, newest first.</summary>
    private static QueryDefinition Newest() => new QueryDefinition { Page = 1, PageSize = Feed.Limit }
        .WithSort<Post>(sort => sort.OrderByDescending(post => post.CreatedAt).ThenByDescending(post => post.Id));

    private static async Task<IReadOnlyList<Feed.Entry>> Entries(IReadOnlyList<Post> roots, string baseUrl, CancellationToken ct)
    {
        if (roots.Count == 0) return [];
        var mentioned = await Mentions.Resolve(roots, baseUrl, ct);
        var ids = roots.Select(root => root.AuthorId).Concat(roots.Select(root => root.EditedBy).OfType<string>()).Distinct(StringComparer.Ordinal).ToList();
        var people = (await Participant.Get(ids, ct)).OfType<Participant>().ToDictionary(row => row.Id, StringComparer.Ordinal);
        var origin = new Uri(baseUrl).GetLeftPart(UriPartial.Authority);
        return Feed.Window(roots.Select(root =>
        {
            var address = PostWrites.TopicAddress(baseUrl, root.TangentId, root.Slug ?? root.Id);
            var html = Media.Placed(Mentions.Html(root.Id, root.Version, root.Body, Mentions.Of(root, mentioned)), baseUrl + "/") + EditedLine(root, people);
            var (author, profile) = Named(root.AuthorId, people, baseUrl);
            return new Feed.Entry(address, root.Title ?? "", Feed.Absolute(html, origin, address), author, profile, root.CreatedAt, root.ChangedAt ?? root.CreatedAt,
                root.CoverId is { } cover ? baseUrl + Media.Reference(cover) : null);
        }));
    }

    /// <summary>Who other than its author last edited a Topic's root and when, so its words are not said to be its author's;
    /// nothing where nobody did.</summary>
    private static string EditedLine(Post root, IReadOnlyDictionary<string, Participant> people) => root.EditedBy is { } by
        ? "<p><em>Edited by " + WebUtility.HtmlEncode(people.TryGetValue(by, out var editor) ? editor.ShownName() : Render.Person.LeftName) + ", "
            + (root.EditedAt ?? root.CreatedAt).UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) + "</em></p>"
        : "";

    private static (string Name, string? Profile) Named(string id, IReadOnlyDictionary<string, Participant> people, string baseUrl)
        => people.TryGetValue(id, out var row)
            ? (row.ShownName(), ProfilePage.AddressOf(baseUrl, row.ShownHandle()))
            : (Render.Person.LeftName, null);
}
