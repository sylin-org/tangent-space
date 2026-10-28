using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Identity;
using Tangent.Infrastructure;
using Tangent.Community;
using Tangent.Application;
using Tangent.Media;
using Tangent.Conversation;
using AngleSharp.Html.Parser;

namespace Tangent.Api;

/// <summary>Explicit page routes share the client shell; data stays behind the domain API.</summary>
[AllowAnonymous]
public sealed class PagesController(IWebHostEnvironment environment, PublicConversationReader publicReader,
    DirectorySummaryReader summaries, TangentGovernance tangents, References references, PostMediaService media) : ControllerBase
{
    [HttpGet("/onboarding/")]
    [HttpGet("/settings")]
    [HttpGet("/notifications")]
    [HttpGet("/following")]
    [HttpGet("/search")]
    [HttpGet("/tangents/")]
    [HttpGet("/tangents/new")]
    [HttpGet("/t/{tangent}/new")]
    [HttpGet("/t/{tangent}/settings")]
    [HttpGet("/t/{tangent}/topics/{topic}/settings")]
    public IActionResult Page()
    {
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(Path.Combine(environment.WebRootPath, "index.html"), "text/html; charset=utf-8");
    }

    [HttpGet("/t/{tangent}/topics")]
    public async Task<IActionResult> TopicCollection(string tangent, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (HasRejectedBearer()) return Unauthorized();
        if (User.Identity?.IsAuthenticated == true) return Page();
        try
        {
            var community = await tangents.Describe(null, tangent, ct);
            if (community is null || community.Kind != TangentKind.Articles) return Page();
            var listing = await summaries.ListTopics(null, tangent, page, ct);
            var current = await tangents.Describe(null, tangent, ct);
            if (current is null || current.Kind != TangentKind.Articles) return HiddenNotFound();
            return PublicDocument(PublicPageRenderer.ArticleCollection(current, listing, await Tangent.Spaces.Space.Get(TangentConstants.SpaceId,ct)));
        }
        catch (TopicRuleViolation) { return BadRequest(); }
        catch (TangentRuleViolation) { return BadRequest(); }
    }

    [HttpGet("/t/{tangent}/topics/{topic}")]
    public async Task<IActionResult> PublicTopic(string tangent, string topic, [FromQuery] string? before = null,
        [FromQuery] string? after = null, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (HasRejectedBearer()) return Unauthorized();
        if (User.Identity?.IsAuthenticated == true) return Page();
        try
        {
            var window = await publicReader.ReadTopic(tangent, topic, before, after,
                PublicConversationReader.MaximumPosts, ct);
            if (window is null) return HiddenNotFound();
            var canonical = window.Topic.Path + (before is { } older ? $"?before={Uri.EscapeDataString(older)}"
                : after is { } newer ? $"?after={Uri.EscapeDataString(newer)}" : "");
            return PublicDocument(PublicPageRenderer.Topic(window, canonical, references.Origin, await ArticleCover(window, ct)));
        }
        catch (ArgumentException) { return BadRequest(); }
    }

    [HttpGet("/t/{tangent}/{post}")]
    public async Task<IActionResult> PublicPost(string tangent, string post, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (HasRejectedBearer()) return Unauthorized();
        if (User.Identity?.IsAuthenticated == true) return Page();
        var document = await publicReader.ReadPost(tangent, post, PublicConversationReader.MaximumPosts, ct);
        return document is null ? HiddenNotFound()
            : PublicDocument(PublicPageRenderer.Post(document,
                $"/t/{Uri.EscapeDataString(tangent)}/{Uri.EscapeDataString(post)}", references.Origin, await ArticleCover(document.Window, ct)));
    }

    private async Task<string?> ArticleCover(PublicPostWindow window, CancellationToken ct)
    {
        if (window.Topic.Presentation != TopicPresentation.Article
            || window.OpeningPost is not { Removed: false, Format: PostContent.Markdown, Html: { } html } opening)
            return null;
        // A cover is an image adopted by the canonical opening Post. Media access rechecks
        // its current public policy; replies and uploads owned by another Post cannot supply it.
        var document = new HtmlParser().ParseDocument("<body>" + html + "</body>");
        foreach (var src in document.QuerySelectorAll("img[src^='/media/']")
                     .Select(image => image.GetAttribute("src")).Distinct().Take(PostMediaService.MaximumReferences))
        {
            if (src is null || !src.StartsWith("/media/", StringComparison.Ordinal)) continue;
            var id = src[7..];
            if (id.Length != 32 || id.Any(character => !char.IsAsciiHexDigit(character))) continue;
            var asset = await media.Read(id, null, ct);
            if (asset?.PostId == opening.Id && asset.TopicKey == window.Topic.Key)
                return references.Origin + src;
        }
        return null;
    }

    [HttpGet("/sign-in/")]
    public IActionResult SignIn([FromQuery(Name = "return")] string? returnTo, [FromQuery] string? provider)
    {
        // The usual sign-in goes straight to the provider's own account chooser.
        // Keep an explicit alternative for custom Atmosphere providers and test identities.
        if (provider == "other") return Page();
        Response.Headers.CacheControl = "no-store";
        var destination = Url.IsLocalUrl(returnTo) && !returnTo!.Any(char.IsControl)
            && !returnTo.StartsWith("/sign-in", StringComparison.OrdinalIgnoreCase) ? returnTo : "/";
        return Redirect("/auth/atproto/challenge?return=" + Uri.EscapeDataString(destination));
    }

    /// <summary>The participant resolver page. Identity-level routing only — suspension and
    /// policy never influence it; the profile API keeps all gating. Non-canonical forms redirect
    /// to the current top identity form; unknown, stale, or ambiguous identifiers miss honestly.</summary>
    [HttpGet("/u/{identifier}")]
    public async Task<IActionResult> Resolve(string identifier, CancellationToken ct)
    {
        var resolved = await ParticipantLookup.TryResolveByIdentifier(identifier, ct);
        if (resolved is null) return NotFound();
        Response.Headers.CacheControl = "no-store";
        // The target is always a stored handle or DID behind a fixed /u/ prefix, never caller input.
        return string.Equals(identifier, resolved.Value.MatchedForm, StringComparison.Ordinal)
            ? PhysicalFile(Path.Combine(environment.WebRootPath, "index.html"), "text/html; charset=utf-8")
            : Redirect("/u/" + Uri.EscapeDataString(resolved.Value.MatchedForm));
    }

    private IActionResult PublicDocument(string html)
    {
        var workspacePath = Path.Combine(environment.WebRootPath, "index.html");
        html = PublicPageRenderer.WithWorkspace(html, System.IO.File.Exists(workspacePath) ? System.IO.File.ReadAllText(workspacePath) : null);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'self'; connect-src 'self'; worker-src 'self' blob:; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data: https:; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, follow";
        return Content(html, "text/html; charset=utf-8");
    }

    private IActionResult HiddenNotFound()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return new EmptyResult();
    }

    private bool HasRejectedBearer()
        => Request.Headers.Authorization.Any(value => value?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            && User.Identity?.IsAuthenticated != true;
}
