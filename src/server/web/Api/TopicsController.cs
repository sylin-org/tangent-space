using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Identity;
using Tangent.Access;
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

    [AllowAnonymous]
    [HttpGet("{topicKey}")]
    public async Task<IActionResult> Describe(string topicKey, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var topic = await topics.Describe(ReadActor(), topicKey, ct);
            return topic is null ? NotFound() : Ok(topic);
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    [TopicMutation]
    [HttpPut("{topicKey}/members/{targetDid}")]
    public async Task<IActionResult> SetMembership(string topicKey, string targetDid, ChangeTopicMembershipRequest request, CancellationToken ct)
        => Outcome(await topics.SetMembership(Actor(), topicKey, targetDid, request.Role, ct));

    [TopicMutation]
    [HttpPut("{topicKey}/topic")]
    public async Task<IActionResult> SetTopic(string topicKey, ChangeTopicDescriptionRequest request, CancellationToken ct)
        => Outcome(await topics.SetTopic(Actor(), topicKey, request.Topic, ct));

    [TopicMutation]
    [HttpPut("{topicKey}/read-audience")]
    public async Task<IActionResult> SetReadAudience(string topicKey, ChangeTopicReadAudienceRequest request, CancellationToken ct)
        => Outcome(await topics.SetReadAudience(Actor(), topicKey, request.Audience, request.PublishExistingHistory, ct));

    [TopicMutation]
    [HttpPatch("{topicKey}/settings")]
    public async Task<IActionResult> SetSettings(string topicKey, ChangeTopicSettingsRequest request, CancellationToken ct)
        => Outcome(await topics.SetSettings(Actor(), topicKey, request.AllowPostEditing, request.IsLocked, request.Title, request.Topic, ct));

    [HttpGet("{topicKey}/access")]
    public async Task<IActionResult> GetAccess(string topicKey, CancellationToken ct)
    {
        try { return Ok(await topics.GetAccess(Actor(), topicKey, null, ct)); }
        catch (TopicRuleViolation rejected) { return StatusCode(rejected.Denial == TopicDenial.NotFound ? 404 : 403, new { reason = rejected.Message }); }
    }

    [TopicMutation]
    [HttpPut("{topicKey}/access")]
    public async Task<IActionResult> SetAccess(string topicKey, AccessMap access, CancellationToken ct)
        => Outcome(await topics.SetAccess(Actor(), topicKey, access, ct));

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
