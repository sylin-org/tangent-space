using Koan.Web.Auth.Connector.Atproto;
using Koan.Data.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Conversation;
using TangentSpace.Participation;
using TangentSpace.Rooms;
using TangentSpace.Rooms.Web;

namespace TangentSpace.Communities.Web;

[ApiController]
[Authorize]
[Route("api/v1/tangents")]
public sealed class VersionedTangentsController(TangentServer hub) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int channelPage = 1, CancellationToken ct = default)
        => await Read(async actor => await hub.Tangents.List(actor, page, channelPage, ct));

    [AllowAnonymous]
    [HttpGet("{tangentId}")]
    public async Task<IActionResult> Get(string tangentId, CancellationToken ct)
        => await Read(async actor => await hub.Tangents.Describe(actor, tangentId, ct));

    [AllowAnonymous]
    [HttpGet("{tangentId}/topics")]
    public async Task<IActionResult> Topics(string tangentId, [FromQuery] int page = 1, CancellationToken ct = default)
        => await Read(async actor => await hub.Topics.ListForTangent(actor, tangentId, page, ct));

    [AllowAnonymous]
    [HttpGet("{tangentId}/topics/{topicId}")]
    public async Task<IActionResult> Topic(string tangentId, string topicId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var actor = ReadActor();
            if (actor is not null && ExpectedParticipantDiffers(actor)) return Conflict(new { reason = "Your signed-in account changed. Reload this page before continuing." });
            var topic = await hub.Topics.Describe(actor, topicId, ct);
            return topic is null || topic.TangentKey != tangentId ? NotFound() : Ok(topic);
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
    }

    [RoomMutation(ParticipationGrants.Post)]
    [HttpPost]
    public Task<IActionResult> Create(CreateTangentRequest request, CancellationToken ct)
        => Execute(actor => hub.Tangents.Create(actor, request.Key, request.Name, request.Description, request.Motto,
            request.Accent, request.Artwork, ct), created: true);

    [RoomMutation(ParticipationGrants.Post)]
    [HttpPost("{tangentId}/topics")]
    public Task<IActionResult> CreateTopic(string tangentId, CreateTangentChannelRequest request, CancellationToken ct)
        => Execute(actor => hub.Tangents.CreateChannel(actor, tangentId, request.Key, request.Title, request.Admission, request.Topic, ct), created: true);

    [RoomMutation]
    [HttpPatch("{tangentId}")]
    public Task<IActionResult> Change(string tangentId, ChangeTangentRequest request, CancellationToken ct)
        => Execute(actor => hub.Tangents.Change(actor, tangentId, request.Name, request.Description, request.Motto,
            request.Accent, request.Artwork, ct, request.AllowMemberTopics));

    [RoomMutation]
    [HttpPatch("{tangentId}/topics/{topicId}/settings")]
    [HttpPatch("{tangentId}/topics/{topicId}")]
    public async Task<IActionResult> ChangeTopic(string tangentId, string topicId, ChangeRoomSettingsRequest request, CancellationToken ct)
    {
        try
        {
            var actor = Actor();
            if (ExpectedParticipantDiffers(actor)) return Conflict(new { reason = "Your signed-in account changed. Reload this page before continuing." });
            var topic = await hub.Topics.Describe(actor, topicId, ct);
            if (topic is null || topic.TangentKey != tangentId) return NotFound();
            return Outcome(await hub.Topics.SetSettings(actor, topicId, request.AllowPostEditing, request.IsLocked, request.Title, request.Topic, ct));
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (RoomRuleViolation rejected) { return StatusCode(Status(rejected.Denial), new { reason = rejected.Message, denial = rejected.Denial }); }
    }

    [AllowAnonymous]
    [HttpGet("{tangentId}/posts/{postId}")]
    public async Task<IActionResult> Post(string tangentId, string postId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var actor = ReadActor();
            if (actor is null) return Unauthorized();
            if (ExpectedParticipantDiffers(actor)) return Conflict(new { reason = "Your signed-in account changed. Reload this page before continuing." });
            // Resolve only the anchor key, then let the room policy and McpWindow enforce access.
            Message? message;
            using (EntityContext.NoCache())
                message = await Message.Get(postId, ct);
            if (message is null) return NotFound();
            var description = await hub.Topics.Describe(actor, message.RoomKey, ct);
            if (description is null || description.TangentKey != tangentId) return NotFound();
            var window = await hub.Posts.McpWindow(actor, message.RoomKey, null, postId, 25, ct);
            return Ok(new { topic = description, window });
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (ArgumentException rejected) { return BadRequest(new { reason = rejected.Message }); }
        catch (TangentRuleViolation rejected) { return StatusCode(Status(rejected.Denial), new { reason = rejected.Message, denial = rejected.Denial }); }
        catch (RoomRuleViolation rejected) { return StatusCode(Status(rejected.Denial), new { reason = rejected.Message, denial = rejected.Denial }); }
    }

    private async Task<IActionResult> Read<T>(Func<string?, Task<T>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var actor = ReadActor();
            if (actor is not null && ExpectedParticipantDiffers(actor)) return Conflict(new { reason = "Your signed-in account changed. Reload this page before continuing." });
            var value = await operation(actor);
            return value is null ? NotFound() : Ok(value);
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (TangentRuleViolation rejected) { return StatusCode(Status(rejected.Denial), new { reason = rejected.Message, denial = rejected.Denial }); }
        catch (RoomRuleViolation rejected) { return StatusCode(Status(rejected.Denial), new { reason = rejected.Message, denial = rejected.Denial }); }
    }

    private async Task<IActionResult> Execute<T>(Func<string, Task<T>> operation, bool created = false)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var actor = Actor();
            if (ExpectedParticipantDiffers(actor)) return Conflict(new { reason = "Your signed-in account changed. Reload this page before continuing." });
            return created ? StatusCode(StatusCodes.Status201Created, await operation(actor)) : Ok(await operation(actor));
        }
        catch (TangentRuleViolation rejected) { return StatusCode(Status(rejected.Denial), new { reason = rejected.Message, denial = rejected.Denial }); }
        catch (RoomRuleViolation rejected) { return StatusCode(Status(rejected.Denial), new { reason = rejected.Message, denial = rejected.Denial }); }
    }

    private string Actor() => ParticipationAccess.Require(User, ParticipationGrants.Manage);

    private string? ReadActor()
    {
        if (User.Identity?.IsAuthenticated == true) return ParticipationAccess.Require(User, ParticipationGrants.Read);
        if (Request.Headers.ContainsKey("Authorization")) throw new UnauthorizedAccessException();
        return null;
    }

    private bool ExpectedParticipantDiffers(string did)
    {
        var expected = Request.Headers["X-Tangent-Participant"];
        return expected.Count > 0 && (expected.Count != 1 || expected[0] != did);
    }

    private static int Status(TangentDenial denial) => denial switch
    {
        TangentDenial.Forbidden => 403, TangentDenial.NotFound => 404, TangentDenial.AlreadyExists => 409, _ => 400
    };

    private static int Status(RoomDenial denial) => denial switch
    {
        RoomDenial.Forbidden => 403, RoomDenial.NotFound => 404, RoomDenial.AlreadyExists => 409, _ => 400
    };

    private static IActionResult Outcome(RoomAdministrationResult result)
        => result.Accepted ? new OkObjectResult(result) : new ObjectResult(result) { StatusCode = Status(result.Denial) };

    private static int Status(RoomDenial? denial) => denial is { } value ? Status(value) : 400;
}
