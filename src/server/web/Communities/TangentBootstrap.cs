using Koan.Data.Core;
using TangentSpace.Site;

namespace TangentSpace.Communities;

internal static class TangentBootstrap
{
    public static async Task<TangentCommunity?> EnsureHome(TangentSite? site, TimeProvider clock, CancellationToken ct)
    {
        if (site is null) return null;
        var home = await TangentCommunity.Get(TangentCommunity.HomeKey, ct);
        if (home is not null) return home;
        home = TangentCommunity.Home(site, clock.GetUtcNow());
        await home.Save(ct);
        return home;
    }
}
