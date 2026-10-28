using Koan.Web.Auth.Connector.Atproto;
using Koan.Data.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Conversation;
using Tangent.Activity;
using Tangent.Identity;
using Tangent.Community;
using Tangent.Stewardship;
using Tangent.Access;

namespace Tangent.Api;

[ApiController]
[Authorize]
[Route("api/v1/tangents")]
public sealed class VersionedTangentsController(TangentServer hub, DirectorySummaryReader summaries,
    TangentMemberDirectory members) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int topicPage = 1, CancellationToken ct = default)
        => await Read(async actor => await summaries.ListTangents(actor, page, topicPage, ct));

    [AllowAnonymous]
    [HttpGet("{tangentId}")]
    public async Task<IActionResult> Get(string tangentId, CancellationToken ct)
        => await Read(async actor => await summaries.DescribeTangent(actor, tangentId, ct));

    [AllowAnonymous]
    [HttpGet("{tangentId}/topics")]
    public async Task<IActionResult> Topics(string tangentId, [FromQuery] int page = 1, CancellationToken ct = default)
        => await Read(async actor => await summaries.ListTopics(actor, tangentId, page, ct));

    [AllowAnonymous]
    [HttpGet("{tangentId}/topics/{topicId}")]
    public async Task<IActionResult> Topic(string tangentId, string topicId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var actor = ReadActor();
            var topic = await summaries.DescribeTopic(actor, tangentId, topicId, ct);
            return topic is null ? NotFound() : Ok(topic);
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    [TopicMutation(ParticipationGrants.Post)]
    [HttpPost]
    public Task<IActionResult> Create(CreateTangentRequest request, CancellationToken ct)
        => Execute(actor => hub.Tangents.Create(actor, request.Key, request.Name, request.Description, request.Motto,
            request.Accent, request.Artwork, ct, kind: request.Kind, readAudience: request.ReadAudience), created: true, grant: ParticipationGrants.Post);

    [TopicMutation(ParticipationGrants.Post)]
    [HttpPost("{tangentId}/topics")]
    public Task<IActionResult> CreateTopic(string tangentId, CreateTangentTopicRequest request, CancellationToken ct)
        => Execute(actor => hub.Tangents.CreateTopic(actor, tangentId, request.Key, request.Title, request.Topic, ct),
            created: true, grant: ParticipationGrants.Post);

    [TopicMutation]
    [HttpPatch("{tangentId}")]
    public Task<IActionResult> Change(string tangentId, ChangeTangentRequest request, CancellationToken ct)
        => Execute(actor => hub.Tangents.Change(actor, tangentId, request.Name, request.Description, request.Motto,
            request.Accent, request.Artwork, ct, request.Kind));

    [HttpGet("{tangentId}/access")]
    public Task<IActionResult> GetAccess(string tangentId, CancellationToken ct)
        => Execute(actor => hub.Tangents.GetAccess(actor, tangentId, ct));

    [HttpGet("{tangentId}/members")]
    public Task<IActionResult> Members(string tangentId, [FromQuery] string? cursor, CancellationToken ct)
        => Execute(actor => members.List(actor, tangentId, cursor, ct));

    [HttpGet("{tangentId}/membership-preview")]
    public Task<IActionResult> PreviewTangentMembership(string tangentId, [FromQuery] string identifier, CancellationToken ct)
        => Execute(actor => members.Preview(actor, tangentId, identifier, ct));

    [TopicMutation]
    [HttpPut("{tangentId}/admission")]
    public Task<IActionResult> SetAdmission(string tangentId, ChangeTangentAdmissionRequest request, CancellationToken ct)
        => Execute(actor => hub.Participants.SetAdmission(actor, tangentId, request.Admission, ct));

    [TopicMutation]
    [HttpPut("{tangentId}/access")]
    public Task<IActionResult> SetAccess(string tangentId, ChangeAccessRequest request, CancellationToken ct)
        => Execute(actor => hub.Tangents.SetAccess(actor, tangentId, request.Access, ct, request.PublishExistingHistory));

    [HttpGet("{tangentId}/topics/{topicId}/access")]
    public async Task<IActionResult> GetTopicAccess(string tangentId, string topicId, CancellationToken ct)
    {
        try { return Ok(await hub.Topics.GetAccess(Actor(), topicId, tangentId, ct)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (TopicRuleViolation rejected) { return TopicProblem(rejected); }
    }

    [TopicMutation]
    [HttpPut("{tangentId}/topics/{topicId}/access")]
    public async Task<IActionResult> SetTopicAccess(string tangentId, string topicId, ChangeAccessRequest request, CancellationToken ct)
    {
        try
        {
            var actor = Actor();
            await hub.Topics.GetAccess(actor, topicId, tangentId, ct);
            var result = await hub.Topics.SetAccess(actor, topicId, request.Access, ct, request.PublishExistingHistory);
            return result.Accepted
                ? Ok(await hub.Topics.GetAccess(actor, topicId, tangentId, ct))
                : Outcome(result);
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (TopicRuleViolation rejected) { return TopicProblem(rejected); }
    }

    [HttpGet("{tangentId}/topics/{topicId}/membership-preview")]
    public async Task<IActionResult> PreviewMembership(string tangentId, string topicId,
        [FromQuery] string identifier, [FromQuery] TopicRole role, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await hub.Topics.PreviewMembership(Actor(), topicId, tangentId, identifier, role, ct)); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (TopicRuleViolation rejected) { return TopicProblem(rejected); }
    }

    [TopicMutation]
    [HttpPatch("{tangentId}/topics/{topicId}/settings")]
    public async Task<IActionResult> ChangeTopic(string tangentId, string topicId, ChangeTopicSettingsRequest request, CancellationToken ct)
    {
        try
        {
            var actor = Actor();
            var topic = await hub.Topics.Describe(actor, topicId, ct);
            if (topic is null || topic.TangentKey != tangentId) return NotFound();
            return Outcome(await hub.Topics.SetSettings(actor, topicId, request.AllowPostEditing, request.IsLocked, request.Title, request.Topic, ct));
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (TopicRuleViolation rejected) { return TopicProblem(rejected); }
    }

    [TopicMutation]
    [HttpPatch("{tangentId}/topics/{topicId}/read-audience")]
    public async Task<IActionResult> ChangeTopicReadAudience(string tangentId, string topicId,
        ChangeTopicReadAudienceRequest request, CancellationToken ct)
    {
        try
        {
            var actor = Actor();
            var topic = await hub.Topics.Describe(actor, topicId, ct);
            if (topic is null || topic.TangentKey != tangentId) return NotFound();
            return Outcome(await hub.Topics.SetReadAudience(actor, topicId, request.Audience,
                request.PublishExistingHistory, ct));
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (TopicRuleViolation rejected) { return TopicProblem(rejected); }
    }

    [AllowAnonymous]
    [HttpGet("{tangentId}/posts/{postId}")]
    public async Task<IActionResult> ReadPost(string tangentId, string postId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var actor = ReadActor();
            if (actor is null) return Unauthorized();
            // Resolve only the anchor key, then let the Topic policy and window enforce access.
            Post? post;
            using (EntityContext.NoCache())
                post = await Post.Get(postId, ct);
            if (post is null) return NotFound();
            var description = await hub.Topics.Describe(actor, post.TopicKey, ct);
            if (description is null || description.TangentKey != tangentId) return NotFound();
            var window = await hub.Posts.ReadWindow(actor, post.TopicKey, null, postId, 25, ct);
            return Ok(new { topic = description, window = PostReadProjection.Of(window) });
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (ArgumentException rejected) { return BadRequest(new { reason = rejected.Message }); }
        catch (TangentRuleViolation rejected) { return StatusCode(Status(rejected.Denial), new { reason = rejected.Message, denial = rejected.Denial }); }
        catch (TopicRuleViolation rejected) { return TopicProblem(rejected); }
    }

    private async Task<IActionResult> Read<T>(Func<string?, Task<T>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var actor = ReadActor();
            var value = await operation(actor);
            return value is null ? NotFound() : Ok(value);
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (TangentRuleViolation rejected) { return StatusCode(Status(rejected.Denial), new { reason = rejected.Message, denial = rejected.Denial }); }
        catch (TopicRuleViolation rejected) { return TopicProblem(rejected); }
    }

    private async Task<IActionResult> Execute<T>(Func<string, Task<T>> operation, bool created = false,
        string grant = ParticipationGrants.Manage)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var actor = ParticipationAccess.Require(User, grant);
            return created ? StatusCode(StatusCodes.Status201Created, await operation(actor)) : Ok(await operation(actor));
        }
        catch (UnauthorizedAccessException) { return StatusCode(StatusCodes.Status403Forbidden); }
        catch (TangentRuleViolation rejected) { return StatusCode(Status(rejected.Denial), new { reason = rejected.Message, denial = rejected.Denial }); }
        catch (TopicRuleViolation rejected) { return TopicProblem(rejected); }
    }

    private string Actor() => ParticipationAccess.Require(User, ParticipationGrants.Manage);

    private string? ReadActor()
    {
        if (User.Identity?.IsAuthenticated == true) return ParticipationAccess.Require(User, ParticipationGrants.Read);
        if (Request.Headers.ContainsKey("Authorization")) throw new UnauthorizedAccessException();
        return null;
    }

    private static int Status(TangentDenial denial) => denial switch
    {
        TangentDenial.Forbidden => 403, TangentDenial.NotFound => 404, TangentDenial.AlreadyExists => 409, _ => 400
    };

    private static int Status(TopicDenial denial) => denial switch
    {
        TopicDenial.Forbidden => 403, TopicDenial.NotFound => 404, TopicDenial.AlreadyExists => 409, _ => 400
    };

    private static IActionResult Outcome(TopicAdministrationResult result)
        => result.Accepted ? new OkObjectResult(result)
            : result.Denial == TopicDenial.HistoryPublicationRequired ? PublicationRequired(result.Reason)
            : new ObjectResult(result) { StatusCode = Status(result.Denial) };

    private static IActionResult TopicProblem(TopicRuleViolation problem)
        => problem.Denial == TopicDenial.HistoryPublicationRequired ? PublicationRequired(problem.Message)
            : new ObjectResult(new { reason = problem.Message, denial = problem.Denial }) { StatusCode = Status(problem.Denial) };

    private static IActionResult PublicationRequired(string reason)
        => new BadRequestObjectResult(new { code = "history_publication_required", reason });

    private static int Status(TopicDenial? denial) => denial is { } value ? Status(value) : 400;
}
