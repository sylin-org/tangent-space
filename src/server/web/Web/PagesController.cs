using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Participants;

namespace TangentSpace.Web;

/// <summary>Explicit page routes share the client shell; data stays behind the domain API.</summary>
[AllowAnonymous]
public sealed class PagesController(IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("/onboarding/")]
    [HttpGet("/sign-in/")]
    [HttpGet("/tangents/")]
    [HttpGet("/t/{tangent}/topics")]
    [HttpGet("/t/{tangent}/topics/{topic}")]
    [HttpGet("/t/{tangent}/{post}")]
    public IActionResult Page()
    {
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(Path.Combine(environment.WebRootPath, "index.html"), "text/html; charset=utf-8");
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
}
