using Koan.Data.Core;
using Microsoft.Extensions.Options;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;

namespace TangentSpace.Site;

// This host-owned operation coordinates two records. Tangent runs one writer process.
public sealed class Arrival(IOptions<SiteOptions> options, TimeProvider clock, PolicyGate gate)
{
    public async Task CheckConfiguration(CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            site?.CheckConfiguredOwner(options.Value);
        }
        finally { gate.Exit(); }
    }

    public async Task Enter(string verifiedDid, string? verifiedHandle, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TangentConstants.ArrivalTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            site?.CheckConfiguredOwner(options.Value);
            var now = clock.GetUtcNow();
            var participant = await Participant.Get(verifiedDid, ct);
            if (participant is null) participant = Participant.FirstArrival(verifiedDid, verifiedHandle, now);
            else participant.Return(verifiedDid, verifiedHandle, now);
            await participant.Save(ct);
            if (site is null && string.Equals(options.Value.OwnerDid, verifiedDid, StringComparison.Ordinal))
                await TangentSite.Establish(options.Value, verifiedDid, now).Save(ct);
            await EntityContext.Commit(ct);
        }
        finally { gate.Exit(); }
    }
}
