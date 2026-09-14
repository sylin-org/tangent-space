using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Participants;
using TangentSpace.Rooms.Web;

namespace TangentSpace.Web;

/// <summary>Explicit page routes share the client shell; data stays behind the domain API.</summary>
[AllowAnonymous]
public sealed class PagesController(IWebHostEnvironment environment, PublicConversationReader publicReader) : ControllerBase
{
    [HttpGet("/onboarding/")]
    [HttpGet("/settings")]
    [HttpGet("/tangents/")]
    [HttpGet("/t/{tangent}/topics")]
    [HttpGet("/t/{tangent}/settings")]
    [HttpGet("/t/{tangent}/topics/{topic}/settings")]
    public IActionResult Page()
    {
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(Path.Combine(environment.WebRootPath, "index.html"), "text/html; charset=utf-8");
    }

    [HttpGet("/t/{tangent}/topics/{topic}")]
    public async Task<IActionResult> PublicTopic(string tangent, string topic, [FromQuery] long? before = null,
        [FromQuery] long? after = null, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        if (HasRejectedBearer()) return Unauthorized();
        if (User.Identity?.IsAuthenticated == true) return Page();
        try
        {
            var window = await publicReader.ReadTopic(tangent, topic, before, after,
                PublicConversationReader.MaximumPosts, ct);
            if (window is null) return HiddenNotFound();
            var canonical = window.Topic.Path + (before is { } older ? $"?before={older}"
                : after is { } newer ? $"?after={newer}" : "");
            return PublicDocument(PublicPageRenderer.Topic(window, canonical));
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
                $"/t/{Uri.EscapeDataString(tangent)}/{Uri.EscapeDataString(post)}"));
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
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'self'; img-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";
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
