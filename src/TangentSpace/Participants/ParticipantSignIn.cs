using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;
using Koan.Web.Auth.Connector.Atproto;
using TangentSpace.Infrastructure;
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
        await arrival.Enter(did, ctx.Identity.FindFirst(AtprotoClaimTypes.Handle)?.Value, ct);
    }
}
