using Koan.Data.Core;
using Microsoft.Extensions.Options;
using TangentSpace.Conversation;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using TangentSpace.Authorization;

namespace TangentSpace.Site;

// This host-owned operation coordinates two records. Tangent runs one writer process.
public sealed class Arrival(IOptions<SiteOptions> options, IOptions<ConversationOptions> conversation, TimeProvider clock, PolicyGate gate,
    TangentSpace.Participants.ParticipantDirectory directory, IAtprotoHandleSource handles, ParticipantProfiles profiles,
    TangentRoles? roles = null)
{
    public async Task CheckConfiguration(CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            if (site is not null) await site.CheckConfiguredOwner(options.Value, ct);
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

    /// <summary>One verified atproto arrival: first arrival enrolls a new participant (GUIDv7
    /// spine, internal identity, atproto identity carrying the handle as its label); a return
    /// matches the atproto identity value and refreshes its label. Enrollment and label refresh
    /// both persist under the directory's mint gate, sharing one discipline with source ingest.</summary>
    public async Task<Participant> Enter(string verifiedDid, string? verifiedHandle, CancellationToken ct)
    {
        // Optional account decoration must not occupy the server-wide policy gate.
        verifiedHandle ??= await handles.HandleOf(verifiedDid, ct);
        Participant participant;
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TangentConstants.ArrivalTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            if (site is not null) await site.CheckConfiguredOwner(options.Value, ct);
            participant = await directory.ArriveAtproto(verifiedDid, verifiedHandle, clock.GetUtcNow(), ct);
            // Arrival establishes participant identity only. Server ownership requires an explicit human declaration.
            await EntityContext.Commit(ct);
            profiles.Request(verifiedDid);
        }
        finally { gate.Exit(); }
        if (roles is not null) await roles.ReconcileParticipant(participant.Id, ct);
        return participant;
    }
}
