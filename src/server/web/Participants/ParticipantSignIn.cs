using System.Security.Claims;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;
using Koan.Web.Auth.Connector.Atproto;
using TangentSpace.Infrastructure;
using TangentSpace.Participation;
using TangentSpace.Site;

namespace TangentSpace.Participants;

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
    }
}
