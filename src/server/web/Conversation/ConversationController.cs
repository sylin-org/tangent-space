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
            return Ok(PostReadProjection.Of(await conversation.History(ParticipationAccess.Require(User, ParticipationGrants.Read), topicKey,
                cursor, ct, from == "start", afterPostId)));
        });

    [HttpGet("updates")]
    public Task<IActionResult> Updates(string topicKey, [FromQuery] string cursor, CancellationToken ct)
        => Execute(async () => Ok(PostReadProjection.Of(await conversation.WaitForUpdates(ParticipationAccess.Require(User, ParticipationGrants.Read), topicKey, cursor, ct))));

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        try { return await operation(); }
        catch (UnauthorizedAccessException) { return StatusCode(403, new { reason = "Your current access does not permit this operation." }); }
        catch (ArgumentException error) { return BadRequest(new { reason = error.Message }); }
    }
}
