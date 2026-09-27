using System.Text.Json;
using Koan.Data.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Identity;

namespace Tangent.Conversation;

[ApiController, Authorize, Route("api/topics/{topicKey}")]
public sealed class ConversationController(TangentServer hub) : ControllerBase
{
    private ConversationService conversation => hub.Posts;

    [HttpGet("posts")]
    public Task<IActionResult> History(string topicKey, [FromQuery] string? cursor, [FromQuery] string? from,
        [FromQuery(Name = "after")] string? afterPostId, CancellationToken ct)
        => Execute(async () =>
        {
            if (from is not null && from != "start") throw new ArgumentException("The supported history origin is 'start'.");
            return Ok(await conversation.History(ParticipationAccess.Require(User, ParticipationGrants.Read), topicKey,
                cursor, ct, from == "start", afterPostId));
        });

    [HttpPost("posts"), ConversationMutation, RequestSizeLimit(16384)]
    public Task<IActionResult> CreatePost(string topicKey, PostCreateRequest input, CancellationToken ct)
        => Execute(async () =>
        {
            var did = ParticipationAccess.Require(User, ParticipationGrants.Post);
            var post = await conversation.CreatePost(did, topicKey, input, ct);
            return Ok(new { post.OperationId, State = "accepted" });
        });

    [HttpPatch("posts/{postId}"), ConversationMutation, RequestSizeLimit(16384)]
    public Task<IActionResult> Edit(string topicKey, string postId, PostChangeRequest input, CancellationToken ct)
        => Execute(async () =>
        {
            var did = await RequireChangeActor(topicKey, postId, ct);
            var result = await conversation.ChangePost(did, topicKey, postId, input.Text, false, ct, facets: input.Facets);
            return Ok(result);
        });

    [HttpDelete("posts/{postId}"), ConversationMutation, RequestSizeLimit(16384)]
    public Task<IActionResult> Delete(string topicKey, string postId, PostDeleteRequest input, CancellationToken ct)
        => Execute(async () =>
        {
            var did = await RequireChangeActor(topicKey, postId, ct);
            var result = await conversation.ChangePost(did, topicKey, postId, null, true, ct);
            return Ok(result);
        });

    [HttpGet("updates")]
    public Task<IActionResult> Updates(string topicKey, [FromQuery] string cursor, CancellationToken ct)
        => Execute(async () => Ok(await conversation.WaitForUpdates(ParticipationAccess.Require(User, ParticipationGrants.Read), topicKey, cursor, ct)));

    [HttpPost("read-position"), ConversationMutation]
    public Task<IActionResult> Acknowledge(string topicKey, ReadAcknowledgement input, CancellationToken ct)
        => Execute(async () => Ok(new { sequence = await conversation.Acknowledge(ParticipationAccess.Require(User, ParticipationGrants.Read), topicKey, input.Cursor, ct) }));

    private async Task<string> RequireChangeActor(string topicKey, string postId, CancellationToken ct)
    {
        var did = ParticipationAccess.Require(User, ParticipationGrants.Read);
        using var fresh = EntityContext.NoCache();
        var post = await Post.Get(postId, ct);
        if (post is null || post.TopicKey != topicKey) throw new ArgumentException("Choose a post in this Topic.");
        ParticipationAccess.Require(User, post.AuthorParticipantId == did ? ParticipationGrants.Post : ParticipationGrants.Manage);
        return did;
    }

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        try { return await operation(); }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { reason = "Your current access does not permit this operation." }); }
        catch (OperationConflictException error) { return Conflict(new { reason = error.Message }); }
        catch (ArgumentException error) { return BadRequest(new { reason = error.Message }); }
    }
}
/// <summary>An edit carries the full composer package (D2a): text plus optional client-verified
/// facets in the same DTO shape the create path uses; absent facets degrade to server re-detection.</summary>
public sealed record PostChangeRequest(string Text, string OperationId, IReadOnlyList<PostFacet>? Facets = null);
public sealed record PostDeleteRequest(string OperationId);
