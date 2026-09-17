using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Identity;
using Tangent.Spaces;
using Tangent.Access;

namespace Tangent.Api;

[ApiController]
public sealed class ServerController(TangentServer hub) : ControllerBase
{
    private ServerGovernance governance => hub.Space;

    [AllowAnonymous, HttpGet("/api/server")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        string? participantId = null;
        if (User.Identity?.IsAuthenticated == true) participantId = ParticipationAccess.Require(User, ParticipationGrants.Welcome);
        return Ok(await governance.Read(participantId, ct));
    }

    [HttpPatch("/api/server"), TopicMutation]
    public async Task<IActionResult> Patch([FromBody] ServerSettingsPatch patch, CancellationToken ct)
    {
        try { return Ok(await governance.Update(ParticipationAccess.Require(User, ParticipationGrants.Welcome), patch, ct)); }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [Authorize, HttpGet("/api/server/access")]
    public async Task<IActionResult> GetAccess(CancellationToken ct)
    {
        try { return Ok(await governance.GetAccess(ParticipationAccess.Require(User, ParticipationGrants.Welcome), ct)); }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("/api/server/access"), TopicMutation]
    public async Task<IActionResult> SetAccess([FromBody] AccessMap access, CancellationToken ct)
    {
        try { return Ok(await governance.SetAccess(ParticipationAccess.Require(User, ParticipationGrants.Welcome), access, ct)); }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }
}

