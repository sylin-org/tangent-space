using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Application;
using Tangent.Activity;
using Tangent.Conversation;
using Tangent.Identity;
using Tangent.Stewardship;

namespace Tangent.Api;

/// <summary>The versioned forum resource API. Both clients enter through the shared request
/// identity scheme: an Authorization header selects the participant credential (never a cookie
/// fallback), its absence selects the browser session. Private responses are never cacheable.</summary>
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

    [HttpPost("topics/{topicKey}/posts")]
    public Task<IActionResult> CreatePost(string topicKey, [FromBody] ExperienceCreatePostRequest request, CancellationToken ct)
        => Mutate(principal => experience.CreatePost(principal, topicKey,
            RequiredRequestId(request.RequestId), request.Text, request.ReplyTo, request.Facets, ct, request.Format));

    [HttpPost("tangents/{tangentKey}/topics")]
    public Task<IActionResult> StartThread(string tangentKey, [FromBody] ExperienceStartThreadRequest request, CancellationToken ct)
        => Mutate(principal => experience.StartThread(principal, tangentKey,
            RequiredRequestId(request.RequestId), request.Title, request.Text, ct, request.Format, request.Audience, request.Subtitle));

    [HttpPatch("posts/{postId}")]
    public Task<IActionResult> EditPost(string postId, [FromBody] ExperienceEditPostRequest request, CancellationToken ct)
        => Mutate(principal => experience.EditPost(principal, postId, request.Text, ct, request.Format));

    [HttpDelete("posts/{postId}")]
    public Task<IActionResult> DeletePost(string postId, CancellationToken ct)
        => Mutate(principal => experience.DeletePost(principal, postId, ct));

    [HttpGet("participants/{*identifier}")]
    public Task<IActionResult> Participant(string identifier, CancellationToken ct)
        => Read(principal => experience.ReadParticipant(principal, identifier, ct));

    [HttpPost("moderation/users/{user}/management")]
    public Task<IActionResult> ManageUser(string user, [FromBody] ExperienceManageUserRequest request, CancellationToken ct)
        => Mutate(principal => experience.ManageUser(principal, user, request.Action,
            request.Reason, request.DurationSeconds, request.Role, request.CaseRef, ct));

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
        => Mutate(principal => experience.ReadPosition(principal, topicKey, request.ReadCursor, ct));

    [HttpPut("tangents/{tangentKey}/membership")]
    public Task<IActionResult> Join(string tangentKey, [FromBody] ExperienceMembershipRequest request, CancellationToken ct)
        => Mutate(principal => experience.Join(principal, tangentKey, ct));

    [HttpDelete("tangents/{tangentKey}/membership")]
    public Task<IActionResult> Leave(string tangentKey, CancellationToken ct)
        => Mutate(principal => experience.Leave(principal, tangentKey, ct));

    [HttpPut("watches")]
    public Task<IActionResult> SetWatch([FromBody] ExperienceWatchRequest request, CancellationToken ct)
        => Mutate(principal => experience.SetWatch(principal, request.ScopeRef, request.Mode, ct));

    [HttpGet("/api/participants/{identifier}/profile")]
    public Task<IActionResult> Profile(string identifier, CancellationToken ct)
        => Read(principal => experience.ParticipantProfile(principal, identifier, ct));

    private async Task<IActionResult> Read<T>(Func<ClaimsPrincipal, Task<ResourceResult<T>>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var response = await operation(User);
            if (response.Problem is { } problem)
                return Problem(problem, problem.Code is ExperienceProblemCodes.CursorExpired or ExperienceProblemCodes.InvalidArguments ? 400 : 403);
            return response.Pending ? StatusCode(202, response.Data) : Ok(response.Data);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (RequestArgumentException error)
        {
            return Problem(ExperienceProblem.Of("invalid_arguments", error.Message, error.Field), 400);
        }
        catch (OperationConflictException error)
        {
            return Problem(ExperienceProblem.Of("request_conflict", error.Message), 409);
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

    private Task<IActionResult> Mutate<T>(Func<ClaimsPrincipal, Task<ResourceResult<T>>> operation)
    {
        var request = Request;
        if (!HttpMethods.IsDelete(request.Method)
            && !string.Equals(request.ContentType?.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<IActionResult>(StatusCode(415, new { reason = "Use application/json." }));
        if (request.Headers.ContainsKey("Authorization"))
        {
            if (!ParticipationAccess.UsesCredential(User))
                return Task.FromResult<IActionResult>(Forbid());
        }
        else if (User.Identity?.IsAuthenticated != true
            || string.IsNullOrWhiteSpace(User.FindFirst(ParticipationConstants.ParticipantClaim)?.Value))
            return Task.FromResult<IActionResult>(Unauthorized());
        else if (request.Headers.Origin.Count != 1
            || !TopicMutationAttribute.IsSameOrigin(request.Headers.Origin[0], request.Scheme, request.Host.Value))
            return Task.FromResult<IActionResult>(StatusCode(403, new { reason = "Writes must come from this space's origin." }));
        return Read(operation);
    }

    private static string RequiredRequestId(string? value)
        => !string.IsNullOrWhiteSpace(value) ? value
            : throw new RequestArgumentException("requestId", "Provide a stable request ID for this write.");

    private IActionResult Problem(ExperienceProblem problem, int status)
        => StatusCode(status, new { problem.Code, problem.Message, problem.Field, problem.Retryable });

}

public sealed record ExperienceCreatePostRequest(string RequestId, string Text, string? ReplyTo,
    IReadOnlyList<Conversation.PostFacet>? Facets = null, string? Format = null);

public sealed record ExperienceReadPositionRequest(string ReadCursor);

public sealed record ExperienceMembershipRequest();

public sealed record ExperienceWatchRequest(string ScopeRef, string Mode);

public sealed record ExperienceReportPostRequest(string RequestId, string PostRef, string ReasonCode, string Statement);

public sealed record ExperienceModerationPreviewRequest(string Action, string Summary, DateTimeOffset? DeferredUntil,
    long ExpectedCaseRevision, string ExpectedSubjectRevision);

public sealed record ExperienceModerationActionRequest(string RequestId, string Action, string Summary,
    DateTimeOffset? DeferredUntil, long ExpectedCaseRevision, string ExpectedSubjectRevision);

public sealed record ExperienceStartThreadRequest(string RequestId, string Title, string Text, string? Format = null, string? Audience = null, string? Subtitle = null);

public sealed record ExperienceEditPostRequest(string Text, string? Format = null);

public sealed record ExperienceManageUserRequest(string Action, string Reason, long? DurationSeconds = null,
    string? Role = null, string? CaseRef = null);
