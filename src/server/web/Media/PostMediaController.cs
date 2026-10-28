using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Api;
using Tangent.Identity;

namespace Tangent.Media;

[ApiController]
public sealed class PostMediaController(TangentServer hub, PostMediaService media) : ControllerBase
{
    [HttpPost("/api/media"), TopicMutation(ParticipationGrants.Post), RequestSizeLimit(29 * 1024 * 1024)]
    public async Task<IActionResult> Upload(PostMediaUpload request, CancellationToken ct)
    {
        try
        {
            var actor = ParticipationAccess.Require(User, ParticipationGrants.Post);
            if (string.IsNullOrWhiteSpace(request.TangentKey) || request.TangentKey.Length > 128)
                return BadRequest(new { reason = "Choose the Tangent for this image." });
            bool allowed;
            if (request.TopicKey is { Length: > 0 } topicKey)
            {
                var topic = await hub.Topics.Describe(actor, topicKey, ct);
                allowed = topic?.TangentKey == request.TangentKey && topic.CanWrite;
            }
            else allowed = (await hub.Tangents.Describe(actor, request.TangentKey, ct))?.CanCreateTopic == true;
            if (!allowed) return StatusCode(403, new { reason = "You cannot attach images here." });
            var asset = await media.Upload(actor, request.TangentKey, string.IsNullOrWhiteSpace(request.TopicKey) ? null : request.TopicKey,
                request.FileName, request.Content ?? [], ct);
            Response.Headers.CacheControl = "no-store";
            return Ok(new { id = asset.Id, url = "/media/" + asset.Id, asset.FileName, asset.ContentType, asset.Size });
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (ArgumentException error) { return BadRequest(new { reason = error.Message }); }
    }

    [AllowAnonymous, HttpGet("/media/{id}")]
    public async Task<IActionResult> Read(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private,no-store";
        Response.Headers.Vary = "Cookie, Authorization";
        Response.Headers.XContentTypeOptions = "nosniff";
        string? actor = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            try { actor = ParticipationAccess.Require(User, ParticipationGrants.Read); }
            catch (UnauthorizedAccessException) { return Unauthorized(); }
        }
        var asset = await media.Read(id, actor, ct);
        return asset is null ? NotFound() : File(await media.Open(asset, ct), asset.ContentType);
    }
}

public sealed record PostMediaUpload(string TangentKey, string? TopicKey, string? FileName, byte[]? Content);
