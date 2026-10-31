using System.Globalization;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>A search of what the place has said at <see cref="SearchPage.Path"/>, one resource both ways
/// (<see cref="SearchPage"/>): a page of Posts, best first, only ever those the reader may view
/// (<see cref="PostSearch"/>), each a line one click from the Post it found. Words or a page that can't be searched answer
/// the same page, saying why, as <c>400</c>. A reader without <see cref="Bit.View"/> on the place receives its gate. Every
/// read is bounded and keyed: capability's rows for the place; the Tangents the reader may view
/// (<see cref="ViewableTangents.All"/>), as many as the place holds; one page of the index; then the roots of the replies
/// and the participants those Posts name. What it answers depends on who reads and changes with every Post, so it is
/// never stored.</summary>
public sealed class SearchController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The query parameter that carries the words.</summary>
    public const string Words = "q";

    /// <summary>The query parameter that carries the number of the page, from one.</summary>
    public const string Number = "page";

    /// <summary>The most Posts one page holds.</summary>
    public const int PageSize = 20;

    [HttpGet(SearchPage.Path)]
    public async Task<IActionResult> Get([FromQuery(Name = Words)] string? q, [FromQuery(Name = Number)] string? page, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var rows = await Capabilities.LoadRows(personId, null, ct);
        var place = rows.For(null);
        if (!place.Exists) return DoorController.Gated(place.Gate(Bit.View) ?? Capabilities.Absent);

        var baseAddress = baseUrl.Resolve(HttpContext);
        var text = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        var address = baseAddress + SearchPage.Path;
        var query = text is null ? "" : "?" + Words + "=" + Uri.EscapeDataString(text);
        var back = baseUrl.PathBase + SearchPage.Path + query;
        var who = await PostWrites.Reader(personId, place, baseAddress, back, ct);
        SearchPage Answered(IReadOnlyList<SearchPage.Hit> hits, string? next, string? previous, string? refused) => new(
            rows.Space.Name,
            baseAddress + "/",
            address + query,
            text,
            hits,
            next,
            previous,
            refused,
            who is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(back) : null,
            who,
            DateOnly.FromDateTime(DateTime.UtcNow));

        if (text is null) return Ok(Answered([], null, null, null));

        var number = 1;
        if (page is not null && !int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out number))
            return BadRequest(Answered([], null, null, PostSearch.NeedsPage));

        var whole = await ViewableTangents.All(personId, ct);
        var tangents = whole.Tangents.ToDictionary(tangent => tangent.Id, StringComparer.Ordinal);
        var answer = await PostSearch.Run(text, tangents.Keys.ToHashSet(StringComparer.Ordinal), whole.Others, number, PageSize, ct);
        if (answer is not PostSearch.Answer.Found found)
            return BadRequest(Answered([], null, null, ((PostSearch.Answer.Refused)answer).Reason));

        string PageAddress(int other) => address + query + (other > 1 ? "&" + Number + "=" + other.ToString(CultureInfo.InvariantCulture) : "");
        return Ok(Answered(
            await Hits(found.Hits, tangents, baseAddress, ct),
            found.More ? PageAddress(number + 1) : null,
            number > 1 ? PageAddress(number - 1) : null,
            null));
    }

    /// <summary>Each hit as the page shows it, from one keyed read of the roots of the replies among them and one of the
    /// participants they name: who wrote them, who else edited them, and whom they mention.</summary>
    private static async Task<IReadOnlyList<SearchPage.Hit>> Hits(IReadOnlyList<PostSearch.Hit> hits, IReadOnlyDictionary<string, Tangents.Tangent> tangents,
        string baseAddress, CancellationToken ct)
    {
        if (hits.Count == 0) return [];
        var posts = hits.Select(hit => hit.Post).ToList();
        var rootIds = posts.Where(post => post.ParentId is not null).Select(post => post.TopicId).Distinct(StringComparer.Ordinal).ToList();
        var roots = rootIds.Count == 0
            ? new Dictionary<string, Post>()
            : (await Post.Get(rootIds, ct)).OfType<Post>().ToDictionary(post => post.Id, StringComparer.Ordinal);
        var people = await People(baseAddress, posts.Select(post => post.AuthorId).Concat(posts.SelectMany(post => post.MentionedIds))
            .Concat(posts.Select(post => post.EditedBy).OfType<string>()), ct);

        return hits.Select(hit =>
        {
            var post = hit.Post;
            var root = post.ParentId is null ? post : roots.GetValueOrDefault(post.TopicId);
            var tangent = tangents[post.TangentId];
            var slug = root?.Slug ?? post.TopicId;
            return new SearchPage.Hit(
                post.Id,
                post.ParentId is null ? PostWrites.Word(post.Kind) : "reply",
                root?.Title ?? "",
                tangent.Name,
                post.ParentId is null ? PostWrites.TopicAddress(baseAddress, tangent.Id, slug) : PostWrites.Address(baseAddress, tangent.Id, slug, post.Id),
                people.TryGetValue(post.AuthorId, out var author) ? author : new TangentPage.Author(post.AuthorId, post.AuthorId, post.AuthorId, "person", null),
                post.CreatedAt,
                Snippet(hit, people),
                TangentPage.EditedOf(post, people));
        }).ToList();
    }

    /// <summary>A hit's words as a reader reads them: the store's snippet read as Markdown to plain text, or, where the
    /// title is what it cut, the Post's first words, since the line above already shows the title.</summary>
    private static IReadOnlyList<SearchPage.Run> Snippet(PostSearch.Hit hit, IReadOnlyDictionary<string, TangentPage.Author> people)
    {
        var post = hit.Post;
        var runs = hit.Snippet is { Length: > 0 } snippet ? SearchPage.Runs(Mentions.PlainFragment(snippet, people)) : [];
        if (runs.Count > 0 && string.Concat(runs.Select(run => run.Text)) != post.Title) return runs;
        return TangentPage.Opening(post, people) is { Length: > 0 } opening ? [new SearchPage.Run(opening, false)] : [];
    }

    /// <summary>Everyone the page shows, in one keyed read, by participant Id, each with their profile's address.</summary>
    private static async Task<IReadOnlyDictionary<string, TangentPage.Author>> People(string baseAddress, IEnumerable<string> ids, CancellationToken ct)
    {
        var wanted = ids.Distinct(StringComparer.Ordinal).ToList();
        return (await Participant.Get(wanted, ct)).OfType<Participant>().ToDictionary(row => row.Id, row =>
        {
            var handle = row.Handle.Length > 0 ? row.Handle : row.Id;
            var name = row.Profile.DisplayName is { Length: > 0 } displayName ? displayName : handle;
            return new TangentPage.Author(row.Id, handle, name, row.Kind.ToString().ToLowerInvariant(), row.Profile.Avatar,
                ProfilePage.AddressOf(baseAddress, handle));
        }, StringComparer.Ordinal);
    }
}
