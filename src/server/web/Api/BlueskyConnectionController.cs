using CarpaNet.OAuth;
using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Conversation;
using Tangent.Identity;
using Tangent.Infrastructure;

namespace Tangent.Api;

/// <summary>Bluesky publishing needs its own account consent. The verified AT session and
/// DPoP tokens stay in Koan's protected server store; this surface exposes only scope state.</summary>
[ApiController, Authorize, Route("api/v1/content/bluesky")]
public sealed class BlueskyConnectionController(AtprotoSessions sessions, BlueskyPublisher publisher) : ControllerBase
{
    [HttpGet("connection")]
    public async Task<IActionResult> Connection(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        string participant;
        try { participant = ParticipationAccess.Require(User, ParticipationGrants.Read); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        var did = User.FindFirst(AtprotoClaimTypes.Did)?.Value;
        if (did is null) return Ok(new { status = "unavailable", actorDid = (string?)null,
            canPublish = false, connectUrl = (string?)null });
        try
        {
            var metadata = await sessions.GetSessionMetadata(did, ct);
            var connected = metadata?.Scopes.Contains(BlueskyPublisher.WriteScope, StringComparer.Ordinal) == true;
            return Ok(new { status = connected ? "connected" : "disconnected", actorDid = did,
                canPublish = connected, connectUrl = "/api/v1/content/bluesky/connect" });
        }
        catch (Exception error) when (error is InvalidOperationException or HttpRequestException
            or OAuthException)
        {
            return Ok(new { status = "reauthorize", actorDid = did, canPublish = false,
                connectUrl = "/api/v1/content/bluesky/connect" });
        }
    }

    [HttpGet("connect")]
    public IActionResult Connect([FromQuery] string? identifier,
        [FromQuery(Name = "return")] string? returnTo)
    {
        Response.Headers.CacheControl = "no-store";
        if (ParticipationAccess.UsesCredential(User)) return Forbid();
        try { ParticipationAccess.Require(User, ParticipationGrants.Read); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        var did = User.FindFirst(AtprotoClaimTypes.Did)?.Value;
        if (did is null) return Forbid();
        var destination = SafeReturn(returnTo);
        if (identifier != did)
            return Redirect("/api/v1/content/bluesky/connect?identifier=" + Uri.EscapeDataString(did)
                + "&return=" + Uri.EscapeDataString(destination));
        var properties = AtprotoChallenge.WithScopes(new AuthenticationProperties
        {
            RedirectUri = destination
        }, "atproto", BlueskyPublisher.WriteScope);
        return Challenge(properties, TangentConstants.AtprotoProvider);
    }

    private static string SafeReturn(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 1024 || value.Contains('\\')
            || value.Any(char.IsControl) || value.Contains('#')
            || !Uri.TryCreate(value, UriKind.Relative, out _)) return "/";
        var path = value.Split('?', 2)[0];
        return path == "/" || path == "/app" || path == "/app/"
            || path == "/settings" || path == "/app/settings"
            || path.StartsWith("/t/", StringComparison.Ordinal)
            || path.StartsWith("/app/t/", StringComparison.Ordinal)
            ? value : "/";
    }

    [HttpPost("publish"), ConversationMutation, RequestSizeLimit(16384)]
    public async Task<IActionResult> Publish([FromBody] BlueskyPublishRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        // A Tangent participant credential must not reuse its holder's stored Bluesky
        // OAuth grant. Remote publishing is an explicit browser account action.
        if (ParticipationAccess.UsesCredential(User)) return Forbid();
        try { return Ok(await publisher.Publish(User, request.PostId, request.RequestId, ct)); }
        catch (UnauthorizedAccessException error) { return StatusCode(403, new { reason = error.Message }); }
        catch (OAuthException) { return StatusCode(403, new { reason = "Connect Bluesky again before publishing.",
            connectUrl = "/api/v1/content/bluesky/connect" }); }
        catch (ArgumentException error) { return BadRequest(new { reason = error.Message }); }
        catch (InvalidOperationException error) { return Conflict(new { reason = error.Message }); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return StatusCode(504, new { reason = "Bluesky did not confirm the Post within 15 seconds. Retry with the same request id." }); }
        catch (HttpRequestException) { return StatusCode(502, new { reason = "Bluesky did not confirm the Post. Retry with the same request id." }); }
    }
}

public sealed record BlueskyPublishRequest(string PostId, string RequestId);
