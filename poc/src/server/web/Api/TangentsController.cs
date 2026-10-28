using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Identity;
using Tangent.Community;
using Tangent.Stewardship;

namespace Tangent.Api;

[ApiController]
[Authorize]
[Route("api/tangents")]
public sealed class TangentsController(TangentServer hub) : ControllerBase
{
    [TopicMutation]
    [HttpPut("{tangentKey}/members/{targetDid}")]
    public Task<IActionResult> SetMembership(string tangentKey, string targetDid, ChangeTangentMembershipRequest request, CancellationToken ct)
        => Execute(actor => hub.Tangents.SetMembership(actor, tangentKey, targetDid, request.Role, ct));

    [TopicMutation]
    [HttpPut("{tangentKey}/roles/{targetIdentifier}")]
    public Task<IActionResult> SetRole(string tangentKey, string targetIdentifier, ChangeParticipantRoleRequest request, CancellationToken ct)
        => Execute(actor => hub.Participants.SetRole(actor, tangentKey, null, targetIdentifier, request.Role, ct));

    private string Actor() => ParticipationAccess.Require(User, ParticipationGrants.Manage);

    private async Task<IActionResult> Execute<T>(Func<string, Task<T>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var actor = Actor();
            return Ok(await operation(actor));
        }
        catch (TangentRuleViolation denied) { return StatusCode(denied.Denial switch
        {
            TangentDenial.Forbidden => StatusCodes.Status403Forbidden,
            TangentDenial.NotFound => StatusCodes.Status404NotFound,
            TangentDenial.AlreadyExists => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        }, new { reason = denied.Message, denial = denied.Denial }); }
        catch (TopicRuleViolation denied) { return StatusCode(denied.Denial switch
        {
            TopicDenial.Forbidden => StatusCodes.Status403Forbidden,
            TopicDenial.NotFound => StatusCodes.Status404NotFound,
            TopicDenial.AlreadyExists => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        }, new { reason = denied.Message, denial = denied.Denial }); }
    }

}
