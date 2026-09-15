using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Participation;
using TangentSpace.Authorization;

namespace TangentSpace.Rooms.Web;

[ApiController]
[Authorize]
[Route("api/rooms")]
public sealed class RoomsController(TangentServer hub) : ControllerBase
{
    private RoomGovernance rooms => hub.Topics;

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await rooms.List(ReadActor(), page, ct)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (RoomRuleViolation rejected) { return BadRequest(new { reason = rejected.Message }); }
    }

    [AllowAnonymous]
    [HttpGet("{roomKey}")]
    public async Task<IActionResult> Describe(string roomKey, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var room = await rooms.Describe(ReadActor(), roomKey, ct);
            return room is null ? NotFound() : Ok(room);
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    [RoomMutation]
    [HttpPut("{roomKey}/members/{targetDid}")]
    public async Task<IActionResult> SetMembership(string roomKey, string targetDid, ChangeRoomMembershipRequest request, CancellationToken ct)
        => Outcome(await rooms.SetMembership(Actor(), roomKey, targetDid, request.Role, ct));

    [RoomMutation]
    [HttpPut("{roomKey}/topic")]
    public async Task<IActionResult> SetTopic(string roomKey, ChangeRoomTopicRequest request, CancellationToken ct)
        => Outcome(await rooms.SetTopic(Actor(), roomKey, request.Topic, ct));

    [RoomMutation]
    [HttpPut("{roomKey}/admission")]
    public async Task<IActionResult> SetAdmission(string roomKey, ChangeRoomAdmissionRequest request, CancellationToken ct)
        => Outcome(await rooms.SetAdmission(Actor(), roomKey, request.Admission, ct));

    [RoomMutation]
    [HttpPut("{roomKey}/read-audience")]
    public async Task<IActionResult> SetReadAudience(string roomKey, ChangeRoomReadAudienceRequest request, CancellationToken ct)
        => Outcome(await rooms.SetReadAudience(Actor(), roomKey, request.Audience, request.PublishExistingHistory, ct));

    [RoomMutation]
    [HttpPatch("{roomKey}/settings")]
    public async Task<IActionResult> SetSettings(string roomKey, ChangeRoomSettingsRequest request, CancellationToken ct)
        => Outcome(await rooms.SetSettings(Actor(), roomKey, request.AllowPostEditing, request.IsLocked, request.Title, request.Topic, ct));

    [HttpGet("{roomKey}/access")]
    public async Task<IActionResult> GetAccess(string roomKey, CancellationToken ct)
    {
        try { return Ok(await rooms.GetAccess(Actor(), roomKey, null, ct)); }
        catch (RoomRuleViolation rejected) { return StatusCode(rejected.Denial == RoomDenial.NotFound ? 404 : 403, new { reason = rejected.Message }); }
    }

    [RoomMutation]
    [HttpPut("{roomKey}/access")]
    public async Task<IActionResult> SetAccess(string roomKey, AccessMap access, CancellationToken ct)
        => Outcome(await rooms.SetAccess(Actor(), roomKey, access, ct));

    [RoomMutation]
    [HttpPut("/api/site/participants/{targetDid}/suspension")]
    public async Task<IActionResult> SetSuspension(string targetDid, ChangeSuspensionRequest request, CancellationToken ct)
        => Outcome(await rooms.SetSuspension(Actor(), targetDid, request.Suspended, ct));

    private string Actor() => ParticipationAccess.Require(User, ParticipationGrants.Manage);

    private string? ReadActor()
    {
        if (User.Identity?.IsAuthenticated == true) return ParticipationAccess.Require(User, ParticipationGrants.Read);
        if (Request.Headers.ContainsKey("Authorization")) throw new UnauthorizedAccessException();
        return null;
    }

    private IActionResult Outcome(RoomAdministrationResult result)
        => result.Accepted ? Ok(result) : StatusCode(result.Denial switch
        {
            RoomDenial.Forbidden => StatusCodes.Status403Forbidden,
            RoomDenial.NotFound => StatusCodes.Status404NotFound,
            RoomDenial.AlreadyExists => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        }, result);
}
