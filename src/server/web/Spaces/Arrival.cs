using Koan.Data.Core;
using Microsoft.Extensions.Options;
using Tangent.Conversation;
using Tangent.Activity;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Community;
using Tangent.Stewardship;
using Tangent.Access;

namespace Tangent.Spaces;

// This host-owned operation coordinates two records. Tangent runs one writer process.
public sealed class Arrival(IOptions<SpaceOptions> options, TimeProvider clock, PolicyGate gate,
    ParticipantDirectory directory, IAtprotoHandleSource handles, ParticipantProfiles profiles)
{
    public async Task CheckConfiguration(CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            if (space is not null) await space.CheckConfiguredOwner(options.Value, ct);
        }
        finally { gate.Exit(); }
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
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            if (space is not null) await space.CheckConfiguredOwner(options.Value, ct);
            participant = await directory.ArriveAtproto(verifiedDid, verifiedHandle, clock.GetUtcNow(), ct);
            // Arrival establishes participant identity only. Server ownership requires an explicit human declaration.
            await EntityContext.Commit(ct);
            profiles.Request(verifiedDid);
        }
        finally { gate.Exit(); }
        return participant;
    }
}
