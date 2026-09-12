using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Participants;

namespace TangentSpace.Web;

/// <summary>Explicit page routes share the client shell; data stays behind the domain API.</summary>
[AllowAnonymous]
public sealed class PagesController(IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("/onboarding/")]
    [HttpGet("/settings")]
    [HttpGet("/t/{tangent}/topics")]
    [HttpGet("/t/{tangent}/topics/{topic}")]
    [HttpGet("/t/{tangent}/{post}")]
    public IActionResult Page()
    {
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(Path.Combine(environment.WebRootPath, "index.html"), "text/html; charset=utf-8");
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

    [HttpGet("/tangents/")]
    public IActionResult Tangents() => Redirect("/#tangent-return");

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
}
