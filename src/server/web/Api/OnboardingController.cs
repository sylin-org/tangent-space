using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Community;
using Tangent.Identity;
using Tangent.Infrastructure;

namespace Tangent.Api;

[ApiController, Authorize]
public sealed class OnboardingController(TangentServer hub, StorageSetup storage) : ControllerBase
{
    [HttpGet("/api/participants/me/profile")]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await hub.Profiles.Read(ParticipationAccess.Require(User, ParticipationGrants.Read), ct));
    }

    [HttpPost("/api/onboarding/tangent"), TopicMutation]
    public async Task<IActionResult> Finish(FirstTangentRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        using var storageLease = await storage.EnterChange(ct);
        if (storage.PendingRestart)
            return Conflict(new { error = "Restart Tangent to apply the selected database before creating the first Tangent." });
        var participantId = ParticipationAccess.Require(User, ParticipationGrants.Manage);
        try
        {
            // Naming the first Tangent is the owner's declaring act. There is no separate
            // confirmation step, so an unclaimed server is claimed by the same press: the
            // step says whose server this becomes, and offers to switch account instead.
            if ((await hub.Space.Read(participantId, ct)).CanClaim)
                await hub.Space.Claim(participantId, User.FindFirst(AtprotoClaimTypes.Did)?.Value, ct);
            return Ok(await hub.Tangents.CompleteOnboarding(participantId, request.Name, request.Description, request.Artwork, request.Skip, ct));
        }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        // TangentRuleViolation is an InvalidOperationException, so it is caught first.
        catch (TangentRuleViolation ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}

public sealed record FirstTangentRequest(string? Name = null, string? Description = null, bool Skip = false,
    string? Artwork = null);
