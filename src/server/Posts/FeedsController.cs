using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>The feeds: a Tangent's at <c>/t/{tangent}/feed.atom</c> and <c>.json</c>, the place's articles at
/// <c>/articles.atom</c> and <c>.json</c>. A feed answers whoever asks as a visitor would read it, so a members-only
/// Tangent has none, for its members as for anyone, and its address answers as one no Tangent has; a private place has
/// none, and <see cref="PrivatePlaceGate"/> answers its visitors first. Each answer is a served file
/// (<see cref="ServedFile"/>), so a reader polling with the tag it holds is a <c>304</c>, judged after who may read it.</summary>
public sealed class FeedsController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    [HttpGet("/t/{tangent}/" + Feeds.TangentSegment + AtomFeed.Extension)]
    [HttpHead("/t/{tangent}/" + Feeds.TangentSegment + AtomFeed.Extension)]
    public async Task<IActionResult> TangentAtom(string tangent, CancellationToken ct)
        => await OfTangent(tangent, ct) is { } feed ? Atom(feed) : PostWrites.Absent();

    [HttpGet("/t/{tangent}/" + Feeds.TangentSegment + JsonFeed.Extension)]
    [HttpHead("/t/{tangent}/" + Feeds.TangentSegment + JsonFeed.Extension)]
    public async Task<IActionResult> TangentJson(string tangent, CancellationToken ct)
        => await OfTangent(tangent, ct) is { } feed ? Json(feed) : PostWrites.Absent();

    [HttpGet(Feeds.ArticlesPath + AtomFeed.Extension)]
    [HttpHead(Feeds.ArticlesPath + AtomFeed.Extension)]
    public async Task<IActionResult> ArticlesAtom(CancellationToken ct)
        => await OfArticles(ct) is { } feed ? Atom(feed) : PostWrites.Absent();

    [HttpGet(Feeds.ArticlesPath + JsonFeed.Extension)]
    [HttpHead(Feeds.ArticlesPath + JsonFeed.Extension)]
    public async Task<IActionResult> ArticlesJson(CancellationToken ct)
        => await OfArticles(ct) is { } feed ? Json(feed) : PostWrites.Absent();

    /// <summary>The Tangent's feed; null where a visitor may not read it. The Tangent's row is read before its overwrites,
    /// so an address no Tangent has takes no place in Koan's cache.</summary>
    private async Task<Feed?> OfTangent(string slug, CancellationToken ct)
    {
        if (await Tangents.Tangent.Get(slug, ct) is not { } tangent) return null;
        var rows = await Capabilities.LoadRows(null, tangent.Id, ct);
        return rows.For(null).Exists ? await Feeds.OfTangent(tangent, rows.Space, Address, ct) : null;
    }

    /// <summary>The place's articles feed; null where a visitor may not read the place. A place a visitor may read has it,
    /// holding no entries while it holds no articles.</summary>
    private async Task<Feed?> OfArticles(CancellationToken ct)
    {
        var rows = await Capabilities.LoadRows(null, null, ct);
        return rows.For(null).Exists ? await Feeds.OfArticles(await ViewableTangents.All(null, ct), rows.Space, Address, ct) : null;
    }

    private IActionResult Atom(Feed feed) => ServedFile.Answer(HttpContext, AtomFeed.Of(feed), AtomFeed.MediaType + "; charset=utf-8");

    private IActionResult Json(Feed feed) => ServedFile.Answer(HttpContext, JsonFeed.Of(feed), JsonFeed.MediaType + "; charset=utf-8");

    private string Address => baseUrl.Resolve(HttpContext);
}
