using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Participation;
using TangentSpace.Rooms.Web;
using TangentSpace.Site;
namespace TangentSpace.Participants.Web;
[ApiController, Authorize]
public sealed class ParticipantSelfController(TangentServer hub) : ControllerBase
{
    private ServerGovernance server => hub.Site;

    [HttpPatch("/api/participants/me"), RoomMutation(ParticipationGrants.Read)]
    public async Task<IActionResult> Declare(ParticipantDeclaration request, CancellationToken ct)
    {
        try { return Ok(await server.Declare(ParticipationAccess.Require(User, ParticipationGrants.Welcome), request.Classification, ct)); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
public sealed record ParticipantDeclaration(ParticipantClassification Classification);
