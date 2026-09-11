using Koan.Data.Core;
using Microsoft.Extensions.Options;
using TangentSpace.Conversation;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Rooms;

namespace TangentSpace.Site;

// This host-owned operation coordinates two records. Tangent runs one writer process.
public sealed class Arrival(IOptions<SiteOptions> options, IOptions<ConversationOptions> conversation, TimeProvider clock, PolicyGate gate)
{
    public async Task CheckConfiguration(CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            site?.CheckConfiguredOwner(options.Value);
            await AdoptLocalStorageDefault(ct);
        }
        finally { gate.Exit(); }
    }

    /// <summary>ADR 0006: when local storage is the configured default, Topics that are still
    /// unprovisioned and have never held a message adopt the local scope. Anything that has
    /// content, an intent or a mapped Space keeps its recorded storage scope.</summary>
    private async Task AdoptLocalStorageDefault(CancellationToken ct)
    {
        if (!conversation.Value.LocalByDefault) return;
        foreach (var room in await Room.Query(value => value.SpaceState == RoomSpaceState.Pending, ct))
        {
            if (room.SpaceUri is not null) continue;
            var state = await RoomConversation.Get(room.Id, ct);
            if (state?.LastSequence > 0) continue;
            if ((await WriteIntent.Query(intent => intent.RoomKey == room.Id, ct)).Count > 0) continue;
            room.SpaceState = RoomSpaceState.Local;
            await room.Save(ct);
        }
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
            // Arrival establishes participant identity only. Server ownership requires an explicit human declaration.
            await EntityContext.Commit(ct);
        }
        finally { gate.Exit(); }
    }
}
