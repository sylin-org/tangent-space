using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Participation;
using TangentSpace.Rooms;

namespace TangentSpace.AtProtocol;

[ApiController, Authorize, Route("api/connections")]
public sealed class SourceStatusController(TangentServer hub) : ControllerBase
{
    private SourceReadiness readiness => hub.Readiness;

    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] string? room = null, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            if (room is not null) Room.CheckKey(room);
            var did = ParticipationAccess.Require(User, ParticipationGrants.Welcome);
            var status = await readiness.Get(did, room, ct);
            return status.Reason is null
                ? Ok(new { did = status.Did, canReadSource = status.CanReadSource, canWriteSource = status.CanWriteSource,
                    state = status.State, connectUrl = status.ConnectUrl })
                : Ok(new { did = status.Did, canReadSource = status.CanReadSource, canWriteSource = status.CanWriteSource,
                    state = status.State, connectUrl = status.ConnectUrl, reason = status.Reason });
        }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { reason = "Your current access does not permit this operation." }); }
        catch (ArgumentException error) { return BadRequest(new { reason = error.Message }); }
    }
}
