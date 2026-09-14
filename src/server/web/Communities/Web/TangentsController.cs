using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Participation;
using TangentSpace.Rooms;
using TangentSpace.Rooms.Web;
using TangentSpace.Authorization;

namespace TangentSpace.Communities.Web;

[ApiController]
[Authorize]
[Route("api/tangents")]
public sealed class TangentsController(TangentServer hub) : ControllerBase
{
    private TangentGovernance tangents => hub.Tangents;

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int channelPage = 1, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var actor = ReadActor();
            return Ok(await tangents.List(actor, page, channelPage, ct));
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (TangentRuleViolation rejected) { return BadRequest(new { reason = rejected.Message }); }
    }

    [RoomMutation(ParticipationGrants.Post)]
    [HttpPost]
    public Task<IActionResult> Create(CreateTangentRequest request, CancellationToken ct)
        => Execute(actor => tangents.Create(actor, request.Key, request.Name, request.Description, request.Motto, request.Accent, request.Artwork, ct), created: true);

    [RoomMutation]
    [HttpPatch("{tangentKey}")]
    public Task<IActionResult> Change(string tangentKey, ChangeTangentRequest request, CancellationToken ct)
        => Execute(actor => tangents.Change(actor, tangentKey, request.Name, request.Description, request.Motto, request.Accent, request.Artwork, ct));

    [HttpGet("{tangentKey}/access")]
    public Task<IActionResult> GetAccess(string tangentKey, CancellationToken ct)
        => Execute(actor => tangents.GetAccess(actor, tangentKey, ct));

    [RoomMutation]
    [HttpPut("{tangentKey}/access")]
    public Task<IActionResult> SetAccess(string tangentKey, AccessMap access, CancellationToken ct)
        => Execute(actor => tangents.SetAccess(actor, tangentKey, access, ct));

    [RoomMutation(ParticipationGrants.Post)]
    [HttpPost("{tangentKey}/channels")]
    public Task<IActionResult> CreateChannel(string tangentKey, CreateTangentChannelRequest request, CancellationToken ct)
        => Execute(actor => tangents.CreateChannel(actor, tangentKey, request.Key, request.Title, request.Admission, request.Topic, ct));

    [RoomMutation]
    [HttpPut("{tangentKey}/members/{targetDid}")]
    public Task<IActionResult> SetMembership(string tangentKey, string targetDid, ChangeTangentMembershipRequest request, CancellationToken ct)
        => Execute(actor => tangents.SetMembership(actor, tangentKey, targetDid, request.Role, ct));

    [RoomMutation]
    [HttpPut("{tangentKey}/roles/{targetIdentifier}")]
    public Task<IActionResult> SetRole(string tangentKey, string targetIdentifier, ChangeCompanionRoleRequest request, CancellationToken ct)
        => Execute(actor => hub.Participants.SetRole(actor, tangentKey, null, targetIdentifier, request.Role, ct));

    private string Actor() => ParticipationAccess.Require(User, ParticipationGrants.Manage);

    private string? ReadActor()
    {
        if (User.Identity?.IsAuthenticated == true) return ParticipationAccess.Require(User, ParticipationGrants.Read);
        if (Request.Headers.ContainsKey("Authorization")) throw new UnauthorizedAccessException();
        return null;
    }

    private async Task<IActionResult> Execute<T>(Func<string, Task<T>> operation, bool created = false)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var actor = Actor();
            return created ? StatusCode(StatusCodes.Status201Created, await operation(actor)) : Ok(await operation(actor));
        }
        catch (TangentRuleViolation denied) { return StatusCode(denied.Denial switch
        {
            TangentDenial.Forbidden => StatusCodes.Status403Forbidden,
            TangentDenial.NotFound => StatusCodes.Status404NotFound,
            TangentDenial.AlreadyExists => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        }, new { reason = denied.Message, denial = denied.Denial }); }
        catch (RoomRuleViolation denied) { return StatusCode(denied.Denial switch
        {
            RoomDenial.Forbidden => StatusCodes.Status403Forbidden,
            RoomDenial.NotFound => StatusCodes.Status404NotFound,
            RoomDenial.AlreadyExists => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        }, new { reason = denied.Message, denial = denied.Denial }); }
    }

}
