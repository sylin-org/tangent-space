using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Identity;
using Tangent.Community;

namespace Tangent.Api;

[ApiController]
[Authorize]
[Route("api/topics")]
public sealed class TopicsController(TangentServer hub) : ControllerBase
{
    private TopicGovernance topics => hub.Topics;

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await topics.List(ReadActor(), page, ct)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (TopicRuleViolation rejected) { return BadRequest(new { reason = rejected.Message }); }
    }

    [TopicMutation]
    [HttpPut("{topicKey}/members/{targetDid}")]
    public async Task<IActionResult> SetMembership(string topicKey, string targetDid, ChangeTopicMembershipRequest request, CancellationToken ct)
        => Outcome(await topics.SetMembership(Actor(), topicKey, targetDid, request.Role, ct));

    [TopicMutation]
    [HttpPut("/api/site/participants/{targetDid}/suspension")]
    public async Task<IActionResult> SetSuspension(string targetDid, ChangeSuspensionRequest request, CancellationToken ct)
        => Outcome(await topics.SetSuspension(Actor(), targetDid, request.Suspended, ct));

    private string Actor() => ParticipationAccess.Require(User, ParticipationGrants.Manage);

    private string? ReadActor()
    {
        if (User.Identity?.IsAuthenticated == true) return ParticipationAccess.Require(User, ParticipationGrants.Read);
        if (Request.Headers.ContainsKey("Authorization")) throw new UnauthorizedAccessException();
        return null;
    }

    private IActionResult Outcome(TopicAdministrationResult result)
        => result.Accepted ? Ok(result) : StatusCode(result.Denial switch
        {
            TopicDenial.Forbidden => StatusCodes.Status403Forbidden,
            TopicDenial.NotFound => StatusCodes.Status404NotFound,
            TopicDenial.AlreadyExists => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        }, result);
}
