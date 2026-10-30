using Koan.Data.Abstractions;
using Koan.Data.Core;

namespace Tangent.Participants;

/// <summary>One handle, one participant: an atproto handle names one account at a time, and a sign-in that verifies a
/// handle is the latest word on who holds it. The command that records a verified handle, inside the writer, takes it
/// from any other participant row that still keeps it; that row shows its DID and answers at its DID's profile address
/// until its own next sign-in verifies a handle again.</summary>
public static class Handles
{
    /// <summary>The most other rows one release reads: the rule leaves at most one to find.</summary>
    public const int ReleaseRead = 8;

    /// <summary>Takes <paramref name="handle"/> from every participant but <paramref name="holderId"/> who keeps it: one
    /// bounded read through the handle index, run inside the command that recorded it. An empty handle holds
    /// nothing.</summary>
    public static async Task Release(string handle, string holderId, CancellationToken ct)
    {
        if (handle.Length == 0) return;
        var others = await Participant.Query(participant => participant.Handle == handle && participant.Id != holderId,
            new QueryDefinition { Page = 1, PageSize = ReleaseRead }, ct);
        foreach (var other in others)
        {
            other.HandleTaken();
            await other.Save(ct);
        }
    }
}
