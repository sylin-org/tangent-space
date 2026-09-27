using System.Security.Claims;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Application;
using Tangent.Activity;
using Tangent.Conversation;
using Tangent.Identity;
using Tangent.Stewardship;
using Tangent.Infrastructure;
using Tangent.Spaces;

namespace Tangent.Api;

/// <summary>The versioned experience API. Both adapters enter through the shared request
/// identity scheme: an Authorization header selects the participant credential (never a cookie
/// fallback), its absence selects the browser session. Private responses are never cacheable;
/// application outcomes arrive as ok/pending/blocked bodies and transport failures as problem
/// objects with matching status codes.</summary>
[ApiController]
[Authorize]
[Route("api/v1/experience")]
public sealed class ExperienceController(ExperienceService experience, LocalContentSource local,
    BlueskyContentSource bluesky) : ControllerBase
{
    [HttpGet("operations")]
    public IActionResult Operations()
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            ParticipationAccess.Require(User, ParticipationGrants.Read);
            return Ok(new { version = ExperienceResponse.Version, operations = ExperienceOperations.Catalog });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    [HttpPost("operations/{id}"), ConversationMutation, RequestSizeLimit(16384)]
    public Task<IActionResult> Invoke(string id, CancellationToken ct)
        => ExperienceOperations.IsSourceOperation(id) ? InvokeSource(id, ct) : Read(async principal =>
        {
            try
            {
                using var body = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
                return await ExperienceOperations.Invoke(experience, principal, id, body.RootElement, ct);
            }
            catch (JsonException) { throw new RequestArgumentException("arguments", "Provide a JSON object of operation arguments."); }
        }, present: true);

    private async Task<IActionResult> InvokeSource(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var participant = ParticipationAccess.Require(User, ParticipationGrants.Read);
            using var body = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
            var args = body.RootElement;
            if (args.ValueKind != JsonValueKind.Object)
                throw new RequestArgumentException("arguments", "Provide a JSON object of operation arguments.");
            string? Value(string field)
            {
                if (!args.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
                return value.ValueKind == JsonValueKind.String ? value.GetString()
                    : throw new RequestArgumentException(field, "Use a string.");
            }
            string Required(string field) => Value(field) is { Length: > 0 } value ? value
                : throw new RequestArgumentException(field, "This argument is required.");
            int Limit()
            {
                if (!args.TryGetProperty("limit", out var value)) return 25;
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var limit)
                    || limit is < 1 or > 25)
                    throw new RequestArgumentException("limit", "Choose 1 to 25 items.");
                return limit;
            }
            object? data;
            string text;
            switch (id)
            {
                case "list_content_sources":
                    data = new { sources = new object[]
                    {
                        new { id = local.Key, status = "ready", read = true, publish = "local" },
                        new { id = bluesky.Key, status = "read_ready", read = true, publish = "account_consent_required" }
                    } };
                    text = "Tangent can read local Topics and selected public Bluesky authors. Bluesky publishing requires separate browser consent.";
                    break;
                case "list_bluesky_authors":
                    var authors = await bluesky.Authors(Limit(), ct);
                    data = new { authors };
                    text = authors.Count == 0 ? "No Bluesky authors have been selected yet."
                        : "Stored Bluesky authors: " + string.Join(", ", authors.Select(author =>
                            author.Handle.Length > 0 ? author.Handle : author.ActorDid));
                    break;
                case "read_bluesky_posts":
                    var page = await bluesky.Read(new ContentQuery(null, null, Required("actorDid"),
                        Value("before"), Value("after"), Limit()), ct);
                    if (page is null) return Ok(new { text = "This Bluesky author has not been selected for import.",
                        structuredContent = (object?)null, status = "blocked", isError = true });
                    data = page;
                    text = page.Posts.Count == 0 ? "No stored Bluesky Posts in this window."
                        : string.Join(" ", page.Posts.Take(3).Select(post => post.Author + ": "
                            + (post.Text.Length > 100 ? post.Text[..100] + "…" : post.Text)))
                            + $" ({page.Posts.Count} Post(s) in this window.)";
                    break;
                case "refresh_bluesky_author":
                    using (EntityContext.NoCache())
                    {
                        var space = await Space.Get(TangentConstants.SpaceId, ct);
                        if (space?.IsOwner(participant) != true) return Forbid();
                    }
                    var result = await bluesky.Refresh(Required("actor"), ct);
                    data = result;
                    text = $"Imported {result.Imported} Post(s) from {result.ActorDid}. "
                        + (result.Backfill == "complete" ? "The current feed is complete."
                            : "Refresh again to continue the bounded import.");
                    break;
                default:
                    throw new RequestArgumentException("operation", "This operation is not available.");
            }
            return Ok(new { text, structuredContent = data, status = "ok", isError = false });
        }
        catch (JsonException) { return Problem(ExperienceProblem.Of("invalid_arguments", "Provide a JSON object of operation arguments."), 400); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (RequestArgumentException error) { return Problem(ExperienceProblem.Of("invalid_arguments", error.Message, error.Field), 400); }
        catch (ArgumentException error) { return Problem(ExperienceProblem.Of("invalid_arguments", error.Message), 400); }
        catch (HttpRequestException) { return StatusCode(502, new { text = "Bluesky could not be reached. Retry the same operation.",
            structuredContent = (object?)null, status = "error", isError = true }); }
    }

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
        => Mutate(principal => experience.CreatePost(principal, topicKey, request.RequestId, request.Text, request.ReplyTo, request.Facets, ct));

    [HttpPost("tangents/{tangentKey}/topics")]
    public Task<IActionResult> StartThread(string tangentKey, [FromBody] ExperienceStartThreadRequest request, CancellationToken ct)
        => Mutate(principal => experience.StartThread(principal, tangentKey, request.RequestId, request.Title, request.Text, ct));

    [HttpPatch("posts/{postRef}")]
    public Task<IActionResult> EditPost(string postRef, [FromBody] ExperienceEditPostRequest request, CancellationToken ct)
        => Mutate(principal => experience.EditPost(principal, postRef, request.Text, ct));

    [HttpDelete("posts/{postRef}")]
    public Task<IActionResult> DeletePost(string postRef, CancellationToken ct)
        => Mutate(principal => experience.DeletePost(principal, postRef, ct));

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

    private async Task<IActionResult> Read(Func<ClaimsPrincipal, Task<ExperienceResponse>> operation, bool present = false)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var response = await operation(User);
            return present
                ? Ok(new { text = ExperienceOperations.Present(response), structuredContent = response,
                    status = response.Status,
                    isError = response.Status is ExperienceStatus.Blocked or ExperienceStatus.Error })
                : Ok(response);
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

    private Task<IActionResult> Mutate(Func<ClaimsPrincipal, Task<ExperienceResponse>> operation) => Read(operation);

    private IActionResult Problem(ExperienceProblem problem, int status)
        => StatusCode(status, new { problem.Code, problem.Message, problem.Field, problem.Retryable });

}

public sealed record ExperienceCreatePostRequest(string RequestId, string Text, string? ReplyTo,
    IReadOnlyList<Conversation.PostFacet>? Facets = null);

public sealed record ExperienceReadPositionRequest(string ReadCursor);

public sealed record ExperienceMembershipRequest();

public sealed record ExperienceWatchRequest(string ScopeRef, string Mode);

public sealed record ExperienceReportPostRequest(string RequestId, string PostRef, string ReasonCode, string Statement);

public sealed record ExperienceModerationPreviewRequest(string Action, string Summary, DateTimeOffset? DeferredUntil,
    long ExpectedCaseRevision, string ExpectedSubjectRevision);

public sealed record ExperienceModerationActionRequest(string RequestId, string Action, string Summary,
    DateTimeOffset? DeferredUntil, long ExpectedCaseRevision, string ExpectedSubjectRevision);

public sealed record ExperienceStartThreadRequest(string RequestId, string Title, string Text);

public sealed record ExperienceEditPostRequest(string Text);

public sealed record ExperienceManageUserRequest(string Action, string Reason, long? DurationSeconds = null,
    string? Role = null, string? CaseRef = null);
