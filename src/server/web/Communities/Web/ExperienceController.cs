using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Experience;
using TangentSpace.Mcp;
using TangentSpace.Participation;
using TangentSpace.Moderation;

namespace TangentSpace.Communities.Web;

/// <summary>The versioned experience API. Both adapters enter through the shared request
/// identity scheme: an Authorization header selects the participant credential (never a cookie
/// fallback), its absence selects the browser session. Private responses are never cacheable;
/// application outcomes arrive as ok/pending/blocked bodies and transport failures as problem
/// objects with matching status codes.</summary>
[ApiController]
[Authorize]
[Route("api/v1/experience")]
public sealed class ExperienceController(ExperienceService experience) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Arrive([FromQuery] string? scopeRef, CancellationToken ct)
        => Read(principal => experience.Arrive(principal, scopeRef, ct));

    [HttpGet("tangents")]
    public Task<IActionResult> Tangents([FromQuery] string? cursor, CancellationToken ct)
        => Read(principal => experience.ListTangents(principal, cursor, ct));

    [HttpGet("tangents/{tangentKey}/topics")]
    public Task<IActionResult> Topics(string tangentKey, [FromQuery] string? cursor, CancellationToken ct)
        => Read(principal => experience.ListTopics(principal, tangentKey, cursor, ct));

    [HttpGet("topics/{topicKey}")]
    public Task<IActionResult> Topic(string topicKey, [FromQuery] string? cursor, [FromQuery] string? aroundPostRef,
        [FromQuery] int limit = 20, CancellationToken ct = default)
        => Read(principal => experience.ReadTopic(principal, topicKey, cursor, aroundPostRef,
            limit is < 1 or > 25 ? 20 : limit, ct));

    [HttpGet("topics/{topicKey}/mentionables")]
    public Task<IActionResult> Mentionables(string topicKey, [FromQuery] string? prefix, CancellationToken ct)
        => Read(principal => experience.Mentionables(principal, topicKey, prefix, ct));

    [HttpGet("updates")]
    public Task<IActionResult> Updates([FromQuery] string? checkpoint, [FromQuery] string? pageCursor,
        [FromQuery] string? scopeRef, [FromQuery] int limit = ExperienceService.DefaultDigestLimit, CancellationToken ct = default)
        => Read(principal => experience.GetUpdates(principal, checkpoint, pageCursor, scopeRef,
            limit is < 1 or > ExperienceService.MaximumDigestLimit ? ExperienceService.DefaultDigestLimit : limit, ct));

    [HttpGet("wait")]
    public Task<IActionResult> Wait([FromQuery] string? checkpoint, [FromQuery] string? sinceRevision,
        [FromQuery] string? scopeRef, CancellationToken ct)
        => Read(principal => experience.Wait(principal, checkpoint, sinceRevision, scopeRef, ct));

    [HttpPost("topics/{topicKey}/posts")]
    public Task<IActionResult> CreatePost(string topicKey, [FromBody] ExperienceCreatePostRequest request, CancellationToken ct)
        => Mutate(principal => experience.CreatePost(principal, topicKey, request.RequestId, request.Text, request.ReplyTo, request.Facets, ct));

    [HttpPost("topics/{topicKey}/reports")]
    public Task<IActionResult> ReportPost(string topicKey, [FromBody] ExperienceReportPostRequest request, CancellationToken ct)
        => Mutate(principal => experience.ReportPost(principal, topicKey, request.RequestId, request.PostRef,
            request.ReasonCode, request.Statement, ct));

    [HttpGet("topics/{topicKey}/moderation/cases")]
    public Task<IActionResult> ModerationCases(string topicKey, [FromQuery] int page = 1, CancellationToken ct = default)
        => Read(principal => experience.ListModerationCases(principal, topicKey, page, ct));

    [HttpGet("moderation/cases/{caseId}")]
    public Task<IActionResult> ModerationCase(string caseId, [FromQuery] int testimonyOffset = 0,
        [FromQuery] int testimonyLimit = 8, [FromQuery] int decisionLimit = 8, CancellationToken ct = default)
        => Read(principal => experience.ReadModerationCase(principal, caseId, testimonyOffset, testimonyLimit, decisionLimit, ct));

    [HttpPost("moderation/cases/{caseId}/previews")]
    public Task<IActionResult> PreviewModeration(string caseId, [FromBody] ExperienceModerationPreviewRequest request, CancellationToken ct)
        => Read(principal => experience.PreviewModerationAction(principal, caseId, request.Action, request.Summary,
            request.DeferredUntil, request.ExpectedCaseRevision, request.ExpectedSubjectRevision, ct));

    [HttpPost("moderation/cases/{caseId}/actions")]
    public Task<IActionResult> ApplyModeration(string caseId, [FromBody] ExperienceModerationActionRequest request, CancellationToken ct)
        => Mutate(principal => experience.ApplyModerationAction(principal, caseId, request.RequestId, request.Action,
            request.Summary, request.DeferredUntil, request.ExpectedCaseRevision, request.ExpectedSubjectRevision, ct));

    [HttpPost("topics/{topicKey}/read-position")]
    public Task<IActionResult> ReadPosition(string topicKey, [FromBody] ExperienceReadPositionRequest request, CancellationToken ct)
        => Mutate(principal => experience.ReadPosition(principal, topicKey, request.ReadCursor, request.RequestId, ct));

    [HttpPut("tangents/{tangentKey}/membership")]
    public Task<IActionResult> Join(string tangentKey, [FromBody] ExperienceMembershipRequest request, CancellationToken ct)
        => Mutate(principal => experience.Join(principal, tangentKey, request.InviteRef, request.RequestId, ct));

    [HttpDelete("tangents/{tangentKey}/membership")]
    public Task<IActionResult> Leave(string tangentKey, [FromQuery] string requestId, CancellationToken ct)
        => Mutate(principal => experience.Leave(principal, tangentKey, requestId, ct));

    [HttpPut("watches")]
    public Task<IActionResult> SetWatch([FromBody] ExperienceWatchRequest request, CancellationToken ct)
        => Mutate(principal => experience.SetWatch(principal, request.ScopeRef, request.Mode, request.RequestId, ct));

    [HttpGet("/api/participants/{identifier}/profile")]
    public Task<IActionResult> Profile(string identifier, CancellationToken ct)
        => Read(principal => experience.ParticipantProfile(principal, identifier, ct));

    [HttpGet("operations/{requestId}")]
    public Task<IActionResult> Operation(string requestId, CancellationToken ct)
        => Read(principal => experience.GetOperation(principal, requestId, ct));

    private async Task<IActionResult> Read(Func<ClaimsPrincipal, Task<ExperienceResponse>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            return Ok(await operation(User));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (McpInvalidArgumentsException error)
        {
            return Problem(ExperienceProblem.Of("invalid_arguments", error.Message, error.Field), 400);
        }
        catch (McpRequestConflictException error)
        {
            return Problem(ExperienceProblem.Of("request_conflict",
                $"{error.RequestId} already identifies a different action. Inspect its receipt before creating another action."), 409);
        }
        catch (ModerationCaseConflictException error)
        {
            return Problem(ExperienceProblem.Of(ExperienceProblemCodes.ModerationConflict, error.Message), 409);
        }
        catch (ArgumentException error)
        {
            return Problem(ExperienceProblem.Of(
                error.Message.Contains("cursor", StringComparison.OrdinalIgnoreCase) ? "cursor_expired" : "invalid_arguments",
                error.Message), 400);
        }
    }

    private Task<IActionResult> Mutate(Func<ClaimsPrincipal, Task<ExperienceResponse>> operation) => Read(operation);

    private IActionResult Problem(ExperienceProblem problem, int status)
        => StatusCode(status, new { problem.Code, problem.Message, problem.Field, problem.Retryable });

}

public sealed record ExperienceCreatePostRequest(string RequestId, string Text, string? ReplyTo,
    IReadOnlyList<Conversation.PostFacet>? Facets = null);

public sealed record ExperienceReadPositionRequest(string ReadCursor, string? RequestId);

public sealed record ExperienceMembershipRequest(string? InviteRef, string RequestId);

public sealed record ExperienceWatchRequest(string ScopeRef, string Mode, string RequestId);

public sealed record ExperienceReportPostRequest(string RequestId, string PostRef, string ReasonCode, string Statement);

public sealed record ExperienceModerationPreviewRequest(string Action, string Summary, DateTimeOffset? DeferredUntil,
    long ExpectedCaseRevision, string ExpectedSubjectRevision);

public sealed record ExperienceModerationActionRequest(string RequestId, string Action, string Summary,
    DateTimeOffset? DeferredUntil, long ExpectedCaseRevision, string ExpectedSubjectRevision);
