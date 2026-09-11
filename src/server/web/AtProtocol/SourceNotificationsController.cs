using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TangentSpace.AtProtocol;

[ApiController, AllowAnonymous]
public sealed class SourceNotificationsController(ServiceAuthentication authentication, SourceNotifications notifications,
    ILogger<SourceNotificationsController> logger) : ControllerBase
{
    [HttpPost("xrpc/" + SpacesOptions.NotifyMethod), RequestSizeLimit(8192)]
    public async Task<IActionResult> Receive(SourceNotificationRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!await authentication.Verify(Request.Headers.Authorization.ToString(), SpacesOptions.NotifyMethod, ct))
        {
            logger.LogWarning("Native Space notification rejected: service authentication failed.");
            return Unauthorized(new { error = "InvalidServiceAuthentication" });
        }
        try
        {
            var accepted = await notifications.Enqueue(request, ct);
            logger.LogInformation("Native Space notification {Result}", accepted ? "queued" : "ignored: Space not found");
            return accepted ? Ok(new { }) : NotFound(new { error = "SpaceNotFound" });
        }
        catch (ArgumentException)
        {
            logger.LogWarning("Native Space notification rejected: invalid request shape.");
            return BadRequest(new { error = "InvalidRequest" });
        }
        catch (InvalidOperationException) { return StatusCode(503, new { error = "InboxUnavailable" }); }
    }
}
