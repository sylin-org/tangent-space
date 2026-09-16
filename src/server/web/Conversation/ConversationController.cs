using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Participation;

namespace TangentSpace.Conversation;

[ApiController, Authorize, Route("api/rooms/{roomKey}")]
public sealed class ConversationController(TangentServer hub) : ControllerBase
{
    private ConversationService conversation => hub.Posts;

    [HttpGet("messages")]
    public Task<IActionResult> History(string roomKey, [FromQuery] string? cursor, [FromQuery] string? from, CancellationToken ct)
        => Execute(async () =>
        {
            if (from is not null && from != "start") throw new ArgumentException("The supported history origin is 'start'.");
            return Ok(await conversation.History(ParticipationAccess.Require(User, ParticipationGrants.Read), roomKey, cursor, ct, from == "start"));
        });

    [HttpPost("messages"), ConversationMutation, RequestSizeLimit(16384)]
    public Task<IActionResult> CreatePost(string roomKey, PostCreateRequest input, CancellationToken ct)
        => Execute(async () =>
        {
            var did = ParticipationAccess.Require(User, ParticipationGrants.Post);
            var post = await conversation.CreatePost(did, roomKey, input, ct);
            return Ok(new { post.OperationId, State = "accepted", post.SourceUri, post.SourceCid });
        });

    [HttpPatch("messages/{messageId}"), ConversationMutation, RequestSizeLimit(16384)]
    public Task<IActionResult> Edit(string roomKey, string messageId, PostChangeRequest input, CancellationToken ct)
        => Execute(async () =>
        {
            var did = await RequireChangeActor(roomKey, messageId, ct);
            var result = await conversation.ChangePost(did, roomKey, messageId, input.Text, false, input.OperationId, ct,
                facets: input.Facets);
            return StatusCode(result.State is "accepted" or "deleted" or "moderated" ? 200 : 409, result);
        });

    [HttpDelete("messages/{messageId}"), ConversationMutation, RequestSizeLimit(16384)]
    public Task<IActionResult> Delete(string roomKey, string messageId, PostDeleteRequest input, CancellationToken ct)
        => Execute(async () =>
        {
            var did = await RequireChangeActor(roomKey, messageId, ct);
            var result = await conversation.ChangePost(did, roomKey, messageId, null, true, input.OperationId, ct);
            return StatusCode(result.State is "accepted" or "deleted" or "moderated" ? 200 : 409, result);
        });

    [HttpGet("updates")]
    public Task<IActionResult> Updates(string roomKey, [FromQuery] string cursor, CancellationToken ct)
        => Execute(async () => Ok(await conversation.WaitForUpdates(ParticipationAccess.Require(User, ParticipationGrants.Read), roomKey, cursor, ct)));

    [HttpPost("read-position"), ConversationMutation]
    public Task<IActionResult> Acknowledge(string roomKey, ReadAcknowledgement input, CancellationToken ct)
        => Execute(async () => Ok(new { sequence = await conversation.Acknowledge(ParticipationAccess.Require(User, ParticipationGrants.Read), roomKey, input.Cursor, ct) }));

    private async Task<string> RequireChangeActor(string roomKey, string messageId, CancellationToken ct)
    {
        var did = ParticipationAccess.Require(User, ParticipationGrants.Read);
        using var fresh = Koan.Data.Core.EntityContext.NoCache();
        var post = await Post.Get(messageId, ct);
        if (post is null || post.RoomKey != roomKey) throw new ArgumentException("Choose a post in this Topic.");
        ParticipationAccess.Require(User, post.AuthorParticipantId == did ? ParticipationGrants.Post : ParticipationGrants.Manage);
        return did;
    }

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        try { return await operation(); }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { reason = "Your current access does not permit this operation." }); }
        catch (WriteConflict) { return Conflict(new { reason = "That operation ID was already used with different content." }); }
        catch (ArgumentException error) { return BadRequest(new { reason = error.Message }); }
    }
}

/// <summary>An edit carries the full composer package (D2a): text plus optional client-verified
/// facets in the same DTO shape the create path uses; absent facets degrade to server re-detection.</summary>
public sealed record PostChangeRequest(string Text, string OperationId, IReadOnlyList<PostFacet>? Facets = null);
public sealed record PostDeleteRequest(string OperationId);
