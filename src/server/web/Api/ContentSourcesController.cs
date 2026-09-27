using Koan.Data.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text;
using Tangent.Conversation;
using Tangent.Identity;
using Tangent.Infrastructure;
using Tangent.Spaces;

namespace Tangent.Api;

/// <summary>Bounded reads across Tangent's local and Bluesky content sources. The Bluesky
/// projection is a public read surface; remote publishing requires separate account consent.</summary>
[ApiController]
[Route("api/v1/content")]
public sealed class ContentSourcesController(LocalContentSource local, BlueskyContentSource bluesky) : ControllerBase
{
    [AllowAnonymous, HttpGet("sources")]
    public IActionResult Sources()
        => Ok(new
        {
            sources = new object[]
            {
                new { id = local.Key, status = "ready", read = true, publish = "local" },
                new { id = bluesky.Key, status = "read_ready", read = true, publish = "account_consent_required" }
            }
        });

    [AllowAnonymous, HttpGet("bluesky/authors")]
    public async Task<IActionResult> BlueskyAuthors([FromQuery] int limit = 25, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (limit is < 1 or > 25) return BadRequest(new { reason = "Choose 1 to 25 authors." });
        return Ok(new { authors = await bluesky.Authors(limit, ct) });
    }

    [AllowAnonymous, HttpGet("/sources/bluesky/{actorDid}")]
    public async Task<IActionResult> BlueskyPage(string actorDid, [FromQuery] string? before,
        [FromQuery] string? after, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var page = await bluesky.Read(new ContentQuery(null, null, actorDid, before, after, 25), ct);
            if (page is null) return NotFound();
            var state = await BlueskySourceState.Get(actorDid, ct);
            var title = WebUtility.HtmlEncode(state?.Handle is { Length: > 0 } handle ? handle : actorDid);
            var html = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"robots\" content=\"noindex,follow\"><link rel=\"icon\" type=\"image/svg+xml\" href=\"/favicon.svg\"><link rel=\"stylesheet\" href=\"/public.css?v=20260913-public1\"><title>From Bluesky — ")
                .Append(title).Append(" · Tangent Space</title></head><body><a class=\"skip\" href=\"#conversation\">Skip to Posts</a><header class=\"masthead\"><a class=\"brand\" href=\"/\"><span class=\"mark\" aria-hidden=\"true\"></span>Tangent Space</a></header><main><nav class=\"breadcrumbs\" aria-label=\"Breadcrumb\"><a href=\"/\">Home</a><span aria-hidden=\"true\">/</span><span aria-current=\"page\">From Bluesky</span></nav><header class=\"topic-hero\"><p class=\"eyebrow\">Public source</p><h1>From Bluesky: ")
                .Append(title).Append("</h1><p class=\"topic-intro\">Selected public Posts stored by Tangent Space. Last refreshed ")
                .Append(WebUtility.HtmlEncode(state?.RefreshedAt.ToString("u") ?? "unknown"))
                .Append(".</p></header><section id=\"conversation\" class=\"conversation\" aria-label=\"Bluesky Posts\">");
            if (page.Posts.Count == 0) html.Append("<p class=\"empty\">No Posts have been imported yet.</p>");
            foreach (var post in page.Posts)
                html.Append("<article class=\"post\"><header><strong>").Append(WebUtility.HtmlEncode(post.Author))
                    .Append("</strong><time datetime=\"").Append(WebUtility.HtmlEncode(post.CreatedAt.ToString("O")))
                    .Append("\">").Append(WebUtility.HtmlEncode(post.CreatedAt.ToString("u")))
                    .Append("</time></header><p class=\"post-text\">").Append(WebUtility.HtmlEncode(post.Text))
                    .Append("</p><footer class=\"post-meta\"><a rel=\"noopener noreferrer\" href=\"")
                    .Append(WebUtility.HtmlEncode(post.Url)).Append("\">View on Bluesky</a></footer></article>");
            html.Append("</section><nav class=\"window-nav\" aria-label=\"Post pages\">");
            var path = "/sources/bluesky/" + Uri.EscapeDataString(actorDid);
            if (page.OlderBefore is { } older)
                html.Append("<a href=\"").Append(path).Append("?before=")
                    .Append(Uri.EscapeDataString(older)).Append("\">Older Posts</a> ");
            if (page.NewerAfter is { } newer)
                html.Append("<a href=\"").Append(path).Append("?after=")
                    .Append(Uri.EscapeDataString(newer)).Append("\">Newer Posts</a>");
            html.Append("</nav></main><footer><p>These public Posts are a local copy of the selected Bluesky author feed.</p></footer></body></html>");
            Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'self'; img-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            Response.Headers["X-Robots-Tag"] = "noindex, follow";
            return Content(html.ToString(), "text/html; charset=utf-8");
        }
        catch (ArgumentException error) { return BadRequest(new { reason = error.Message }); }
    }

    [AllowAnonymous, HttpGet("{source}/posts")]
    public async Task<IActionResult> Posts(string source, [FromQuery] string? tangentKey,
        [FromQuery] string? topicKey, [FromQuery] string? actorDid, [FromQuery] string? before,
        [FromQuery] string? after, [FromQuery] int limit = 25, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (limit is < 1 or > 25) return BadRequest(new { reason = "Choose 1 to 25 Posts." });
        var adapter = source switch
        {
            "local" => (IContentSource)local,
            "bluesky" => bluesky,
            _ => null
        };
        if (adapter is null) return NotFound();
        try
        {
            var page = await adapter.Read(new ContentQuery(tangentKey, topicKey, actorDid, before, after, limit), ct);
            return page is null ? NotFound() : Ok(page);
        }
        catch (ArgumentException error) { return BadRequest(new { reason = error.Message }); }
    }

    [Authorize, TopicMutation(ParticipationGrants.Read), HttpPost("bluesky/authors/{actorDid}/refresh")]
    public async Task<IActionResult> Refresh(string actorDid, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var participant = ParticipationAccess.Require(User, ParticipationGrants.Read);
            using var fresh = EntityContext.NoCache();
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            if (space?.IsOwner(participant) != true) return Forbid();
            return Ok(await bluesky.Refresh(actorDid, ct));
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException error) { return BadRequest(new { reason = error.Message }); }
        catch (HttpRequestException) { return StatusCode(502, new { reason = "Bluesky could not be reached. Retry this refresh." }); }
    }
}
