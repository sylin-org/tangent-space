using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Identity;
using Tangent.Access;
using Tangent.Community;

namespace Tangent.Api;

[ApiController]
[Authorize]
[Route("api/rooms")]
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

    [AllowAnonymous]
    [HttpGet("{roomKey}")]
    public async Task<IActionResult> Describe(string roomKey, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var topic = await topics.Describe(ReadActor(), roomKey, ct);
            return topic is null ? NotFound() : Ok(topic);
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    [TopicMutation]
    [HttpPut("{roomKey}/members/{targetDid}")]
    public async Task<IActionResult> SetMembership(string roomKey, string targetDid, ChangeTopicMembershipRequest request, CancellationToken ct)
        => Outcome(await topics.SetMembership(Actor(), roomKey, targetDid, request.Role, ct));

    [TopicMutation]
    [HttpPut("{roomKey}/topic")]
    public async Task<IActionResult> SetTopic(string roomKey, ChangeTopicDescriptionRequest request, CancellationToken ct)
        => Outcome(await topics.SetTopic(Actor(), roomKey, request.Topic, ct));

    [TopicMutation]
    [HttpPut("{roomKey}/admission")]
    public async Task<IActionResult> SetAdmission(string roomKey, ChangeTopicAdmissionRequest request, CancellationToken ct)
        => Outcome(await topics.SetAdmission(Actor(), roomKey, request.Admission, ct));

    [TopicMutation]
    [HttpPut("{roomKey}/read-audience")]
    public async Task<IActionResult> SetReadAudience(string roomKey, ChangeTopicReadAudienceRequest request, CancellationToken ct)
        => Outcome(await topics.SetReadAudience(Actor(), roomKey, request.Audience, request.PublishExistingHistory, ct));

    [TopicMutation]
    [HttpPatch("{roomKey}/settings")]
    public async Task<IActionResult> SetSettings(string roomKey, ChangeTopicSettingsRequest request, CancellationToken ct)
        => Outcome(await topics.SetSettings(Actor(), roomKey, request.AllowPostEditing, request.IsLocked, request.Title, request.Topic, ct));

    [HttpGet("{roomKey}/access")]
    public async Task<IActionResult> GetAccess(string roomKey, CancellationToken ct)
    {
        try { return Ok(await topics.GetAccess(Actor(), roomKey, null, ct)); }
        catch (TopicRuleViolation rejected) { return StatusCode(rejected.Denial == TopicDenial.NotFound ? 404 : 403, new { reason = rejected.Message }); }
    }

    [TopicMutation]
    [HttpPut("{roomKey}/access")]
    public async Task<IActionResult> SetAccess(string roomKey, AccessMap access, CancellationToken ct)
        => Outcome(await topics.SetAccess(Actor(), roomKey, access, ct));

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
