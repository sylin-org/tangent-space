using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.AtProtocol;
using TangentSpace.Participation;

namespace TangentSpace.Rooms.Web;

[ApiController]
[Authorize]
[Route("api/rooms")]
public sealed class RoomsController(TangentServer hub) : ControllerBase
{
    private RoomGovernance rooms => hub.Topics;
    private SpacesService spaces => hub.Source;

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
    [HttpPost]
    public async Task<IActionResult> Create(CreateRoomRequest request, CancellationToken ct)
        => Outcome(await rooms.Create(Actor(), request.Key, request.Title, request.Admission, ct));

    [RoomMutation]
    [HttpPost("{roomKey}/provision")]
    public async Task<IActionResult> Provision(string roomKey, CancellationToken ct)
    {
        try { return Outcome(await spaces.Provision(Actor(), roomKey, ct)); }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { reason = "Only the current owner can provision this room." }); }
        catch (Exception failure) when (failure is SpacesUnavailable or InvalidDataException or HttpRequestException)
        {
            return StatusCode(503, new { reason = "The real Space could not be confirmed. Check the site connection and retry provisioning." });
        }
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
    [HttpPatch("{roomKey}/settings")]
    public async Task<IActionResult> SetSettings(string roomKey, ChangeRoomSettingsRequest request, CancellationToken ct)
        => Outcome(await rooms.SetSettings(Actor(), roomKey, request.AllowPostEditing, request.IsLocked, request.Title, request.Topic, ct));

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
            RoomDenial.AlreadyExists or RoomDenial.PolicyChanged or RoomDenial.SpaceAlreadyMapped => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        }, result);
}
