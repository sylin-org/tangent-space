using System.Security.Claims;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Extensions;
using Koan.Web.Auth.Flow;
using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authentication;
using Tangent.Activity;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Spaces;

namespace Tangent.Identity;

public sealed class ParticipantSignIn(Arrival arrival) : IKoanAuthFlowHandler
{
    public int Priority => 1100;

    public async Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        var did = ctx.Identity.FindFirst(AtprotoClaimTypes.Did)?.Value;
        if (ctx.Provider != TangentConstants.AtprotoProvider || string.IsNullOrWhiteSpace(did))
        {
            ctx.Reject("Tangent requires a verified AT Protocol account.");
            return;
        }
        var participant = await arrival.Enter(did, ctx.Identity.FindFirst(AtprotoClaimTypes.Handle)?.Value, ct);
        // The cookie carries the GUID spine claim alongside the atproto claims this sign-in proved;
        // bearer principals mint the same pair in ParticipationCredentials.
        ctx.Identity.AddClaim(new Claim(ParticipationConstants.ParticipantClaim, participant.Id));
        // Token-only sessions (docs/DECISIONS.md, 11 September 2026): the browser stores only
        // the token, so the cookie gets a fresh session claim, and the session this sign-in
        // replaces — the request still carries its cookie — is told who the browser now is on
        // any live connection it holds. Best effort: a failed push must never fail sign-in.
        var session = Guid.CreateVersion7().ToString("N");
        ctx.Identity.AddClaim(new Claim(ParticipationConstants.SessionClaim, session));
        try
        {
            var previous = await ctx.HttpContext.AuthenticateAsync(AuthenticationExtensions.CookieScheme);
            var replaced = previous.Succeeded ? previous.Principal?.FindFirst(ParticipationConstants.SessionClaim)?.Value : null;
            if (!string.IsNullOrEmpty(replaced) && replaced != session)
            {
                var server = ctx.Services.GetRequiredService<TangentServer>();
                server.Live.Replaced(replaced, new LiveSessions.Identity(participant.Id,
                    await server.Directory.BestLabel(participant.Id, ct), "Your signed-in account changed."));
            }
        }
        catch (Exception) { /* the replaced session learns nothing; its page keeps its own routing */ }
    }
}
