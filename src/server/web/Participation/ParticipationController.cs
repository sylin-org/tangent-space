using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CookieAuthentication = Koan.Web.Auth.Extensions.AuthenticationExtensions;

namespace TangentSpace.Participation;

[ApiController, Authorize, Route("api/participation/credentials"), RequestSizeLimit(8192)]
public sealed class ParticipationController(ParticipationCredentials credentials) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var cookie = await Cookie(false);
        if (cookie is null) return Unauthorized();
        return Ok(await credentials.List(cookie, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Enroll([FromBody] CredentialEnrollmentRequest request, CancellationToken ct)
    {
        var cookie = await Cookie(true);
        if (cookie is null) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await credentials.Enroll(cookie, request, ct)); }
        catch (ArgumentException error) { return BadRequest(new { error = error.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("{id}/revoke")]
    public async Task<IActionResult> Revoke(string id, [FromBody] CredentialRevocationRequest request, CancellationToken ct)
    {
        var cookie = await Cookie(true);
        if (cookie is null) return Unauthorized();
        return await credentials.Revoke(cookie, id, ct) ? NoContent() : NotFound();
    }

    private async Task<ClaimsPrincipal?> Cookie(bool mutation)
    {
        if (Request.Headers.ContainsKey("Authorization")) return null;
        if (mutation && (!Request.HasJsonContentType() || Request.Headers.Origin.Count != 1
            || !string.Equals(Request.Headers.Origin.ToString(), $"{Request.Scheme}://{Request.Host}", StringComparison.OrdinalIgnoreCase)
            || (Request.Headers.TryGetValue("Sec-Fetch-Site", out var site) && site != "same-origin"))) return null;
        var result = await HttpContext.AuthenticateAsync(CookieAuthentication.CookieScheme);
        if (!result.Succeeded || result.Principal is null) return null;
        try { ParticipationAccess.EnrollmentDid(result.Principal); return result.Principal; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
