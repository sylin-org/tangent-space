using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Communities;
using TangentSpace.Participation;
using TangentSpace.Rooms.Web;

namespace TangentSpace.Web;

[ApiController, Authorize]
public sealed class OnboardingController(TangentServer hub) : ControllerBase
{
    [HttpGet("/api/participants/me/profile")]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await hub.Profiles.Read(ParticipationAccess.Require(User, ParticipationGrants.Read), ct));
    }

    [HttpPost("/api/onboarding/tangent"), RoomMutation]
    public async Task<IActionResult> Finish(FirstTangentRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var did = ParticipationAccess.Require(User, ParticipationGrants.Manage);
        if (request.ExpectedDid != did) return Conflict(new { error = "Your account changed. Reload before continuing." });
        try { return Ok(await hub.Tangents.CompleteOnboarding(did, request.Name, request.Description, request.Skip, ct)); }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        catch (TangentRuleViolation ex) { return BadRequest(new { error = ex.Message }); }
    }
}

public sealed record FirstTangentRequest(string ExpectedDid, string? Name = null, string? Description = null, bool Skip = false);
