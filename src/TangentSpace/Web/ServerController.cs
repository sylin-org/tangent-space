using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Participation;
using TangentSpace.Site;

namespace TangentSpace.Web;

[ApiController]
public sealed class ServerController(TangentServer hub) : ControllerBase
{
    private ServerGovernance governance => hub.Site;

    [AllowAnonymous, HttpGet("/api/server")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        string? did = null;
        if (User.Identity?.IsAuthenticated == true) did = ParticipationAccess.Require(User, ParticipationGrants.Welcome);
        return Ok(await governance.Read(did, ct));
    }

    [HttpPatch("/api/server"), TangentSpace.Rooms.Web.RoomMutation]
    public async Task<IActionResult> Patch([FromBody] ServerSettingsPatch patch, CancellationToken ct)
    {
        try { return Ok(await governance.Update(ParticipationAccess.Require(User, ParticipationGrants.Welcome), patch, ct)); }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("/api/server/claim"), TangentSpace.Rooms.Web.RoomMutation]
    public async Task<IActionResult> Claim([FromBody] ClaimRequest request, CancellationToken ct)
    {
        var did = ParticipationAccess.Require(User, ParticipationGrants.Welcome);
        if (request.ExpectedDid is not null && request.ExpectedDid != did)
            return Conflict(new { error = "Your account changed. Reload before confirming ownership." });
        try { return Ok(await governance.Claim(did, request.HumanDeclaration, ct)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}

public sealed record ClaimRequest(bool HumanDeclaration, string? ExpectedDid = null);
