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
/// visitor may read it at all is the caller's. Reads only and takes no lock: at most <see cref="Feed.Limit"/> Posts, in one
/// query through <c>ix_post_started</c> or <c>ix_post_articles</c>, and everyone they name by key.</summary>
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

    /// <summary>The feed of the place's articles in the Tangents <paramref name="tangents"/> names as a visitor's. The store
    /// is told the smaller of the two sets, and the window keeps only articles among those a visitor may view, so a
    /// Tangent that changed while they were read never shows.</summary>
    public static async Task<Feed> OfArticles(ViewableTangents.Whole tangents, TangentSpace space, string baseUrl, CancellationToken ct)
    {
        var viewable = tangents.Tangents.Select(tangent => tangent.Id).ToHashSet(StringComparer.Ordinal);
        var query = Newest();
        if (tangents.Others.Count > 0 && viewable.Count > 0)
        {
            var tangent = FieldPath.Of(nameof(Post.TangentId));
            query = query with
            {
                Filter = viewable.Count <= tangents.Others.Count
                    ? Filter.On(tangent, FilterOperator.In, FilterValue.Many([.. viewable]))
                    : Filter.On(tangent, FilterOperator.Nin, FilterValue.Many([.. tangents.Others]))
            };
        }

        var articles = viewable.Count == 0
            ? []
            : await Post.Query(post => post.Kind == PostKind.Article && post.ParentId == null && !post.Removed, query, ct);
        return new Feed(ArticlesTitle(space.Name), space.Description, baseUrl + "/", ArticlesAddress(baseUrl),
            await Entries(articles.Where(article => viewable.Contains(article.TangentId)).ToList(), baseUrl, ct));
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
            var html = Mentions.Html(root.Id, root.Version, root.Body, Mentions.Of(root, mentioned)) + EditedLine(root, people);
            var (author, profile) = Named(root.AuthorId, people, baseUrl);
            return new Feed.Entry(address, root.Title ?? "", Feed.Absolute(html, origin, address), author, profile, root.CreatedAt, root.ChangedAt ?? root.CreatedAt);
        }));
    }

    /// <summary>Who other than its author last edited a Topic's root and when, so its words are not said to be its author's;
    /// nothing where nobody did.</summary>
    private static string EditedLine(Post root, IReadOnlyDictionary<string, Participant> people) => root.EditedBy is { } by
        ? "<p><em>Edited by " + WebUtility.HtmlEncode(people.TryGetValue(by, out var editor) ? Shown(editor) : by) + ", "
            + (root.EditedAt ?? root.CreatedAt).UtcDateTime.ToString("d MMMM yyyy", CultureInfo.InvariantCulture) + "</em></p>"
        : "";

    private static (string Name, string? Profile) Named(string id, IReadOnlyDictionary<string, Participant> people, string baseUrl)
        => people.TryGetValue(id, out var row)
            ? (Shown(row), ProfilePage.AddressOf(baseUrl, row.Handle.Length > 0 ? row.Handle : row.Id))
            : (id, null);

    /// <summary>The name a participant is shown by: the profile's display name, else the handle, else their Id.</summary>
    private static string Shown(Participant row)
        => row.Profile.DisplayName is { Length: > 0 } name ? name : row.Handle.Length > 0 ? row.Handle : row.Id;
}
