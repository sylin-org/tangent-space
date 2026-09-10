using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Communities;

/// <summary>One idempotent in-transaction migration from the old site/flat-room shape.</summary>
internal static class TangentBootstrap
{
    private static readonly QueryDefinition rooms = new() { Sort = SortSpecParser.ParseStrict<Room>(nameof(Room.Id)), CountStrategy = CountStrategy.Exact };

    public static async Task<TangentCommunity?> EnsureHome(TangentSite? site, TimeProvider clock, CancellationToken ct)
    {
        if (site is null) return null;
        var home = await TangentCommunity.Get(TangentCommunity.HomeKey, ct);
        if (home is null)
        {
            home = TangentCommunity.Home(site, clock.GetUtcNow());
            await home.Save(ct);
        }
        if (home.LegacyRoomsAssigned) return home;
        // Assign every pre-existing flat room exactly once. New channels are always created
        // with their own TangentKey, so a resumed bootstrap cannot re-home them.
        var hadLegacyRooms = false;
        for (var page = 1; ; page++)
        {
            var listed = await Room.AllWithCount(rooms.WithPagination(page, 100), ct);
            hadLegacyRooms |= listed.Items.Count != 0;
            foreach (var room in listed.Items)
            {
                room.TangentKey = TangentCommunity.HomeKey;
                await room.Save(ct);
            }
            if (!listed.HasNextPage) break;
        }
        home.LegacyRoomsAssigned = true;
        // An existing flat-room installation is already established; an empty new host
        // remains in the owner welcome until its placeholder home card is configured.
        home.SetupComplete = hadLegacyRooms;
        await home.Save(ct);
        return home;
    }
}
