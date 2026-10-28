using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tangent.Api;

[ApiController, AllowAnonymous, Route("api/v1/public/tangents/{tangentId}/posts")]
public sealed class PublicPostsController(PublicConversationReader reader) : ControllerBase
{
    [HttpGet("{postId}")]
    public async Task<IActionResult> Read(string tangentId, string postId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (Request.Headers.Authorization.Any(value => value?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            && User.Identity?.IsAuthenticated != true) return Unauthorized();
        try
        {
            var document = await reader.ReadPost(tangentId, postId, PublicConversationReader.MaximumPosts, ct);
            return document is null ? NotFound() : Ok(document);
        }
        catch (ArgumentException error) { return BadRequest(new { reason = error.Message }); }
    }
}
