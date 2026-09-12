using System.Net;
using Koan.Data.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Communities;
using TangentSpace.Participation;

namespace TangentSpace.Mcp;

/// <summary>Invitations are links to review, never GET requests that silently join an account.</summary>
[ApiController, Route("invite/{invitationId}"), RequestSizeLimit(1024)]
public sealed class McpInvitationController(TangentServer hub, TimeProvider clock) : ControllerBase
{
    private CompanionGovernance companions => hub.Participants;

    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Review(string invitationId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!ValidId(invitationId)) return NotFound();
        var did = User.Identity?.IsAuthenticated == true ? ParticipationAccess.Require(User, ParticipationGrants.Read) : null;
        if (did is null)
            return Page("A place is waiting for you", "Sign in with the account that received this invitation.",
                $"<form action='/auth/atproto/challenge' method='get'><label>Your AT handle <input name='identifier' required autocomplete='username'></label>" +
                $"<input type='hidden' name='return' value='/invite/{invitationId}'><button>Sign in</button></form>");
        using var fresh = EntityContext.NoCache();
        var invitation = await TangentInvitation.Get(invitationId, ct);
        if (invitation is null || invitation.RecipientParticipantId != did || !invitation.Usable(clock.GetUtcNow()))
            return Page("This invitation isn't available", "It may have expired, been used, or belong to another account.", "<a href='/'>Return to Tangent</a>");
        // The recipient can review the name carried by their own still-live invitation.
        var tangent = await TangentCommunity.Get(invitation.TangentKey, ct);
        if (tangent is null) return NotFound();
        return Page("You're invited to " + tangent.Name, "Join with your signed-in account when you're ready.",
            $"<button id='join' data-invitation='{invitationId}'>Join Tangent</button>" +
            "<p id='status' role='status'></p><script src='/invitation.js' defer></script>");
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Accept(string invitationId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        // Cookie flow: exact origin + JSON; bearer clients use JoinTangent instead.
        if (Request.Headers.ContainsKey("Authorization") || !Request.HasJsonContentType()
            || Request.Headers.Origin.Count != 1
            || !string.Equals(Request.Headers.Origin.ToString(), $"{Request.Scheme}://{Request.Host}", StringComparison.OrdinalIgnoreCase))
            return StatusCode(403);
        var did = ParticipationAccess.Require(User, ParticipationGrants.Read);
        if (!ValidId(invitationId)) return StatusCode(403);
        using var fresh = EntityContext.NoCache();
        var invitation = await TangentInvitation.Get(invitationId, ct);
        if (invitation is null || invitation.RecipientParticipantId != did) return NotFound();
        try
        {
            await companions.Join(did, invitation.TangentKey, invitationId, ct);
            return Ok(new { destination = "/?tangent=" + Uri.EscapeDataString(invitation.TangentKey) });
        }
        catch (TangentRuleViolation) { return StatusCode(403, new { reason = "This invitation cannot be used by your account right now." }); }
    }

    private static bool ValidId(string id) => id.Length == 32 && id.All(char.IsAsciiHexDigit);

    private ContentResult Page(string title, string description, string controls)
        => Content("<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'>" +
            "<title>Tangent invitation</title><link rel='stylesheet' href='/invitation.css'><main><p>TANGENT SPACE</p><h1>" +
            WebUtility.HtmlEncode(title) + "</h1><p>" + WebUtility.HtmlEncode(description) + "</p>" + controls + "</main></html>", "text/html");
}
