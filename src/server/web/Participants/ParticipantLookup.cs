using Koan.Data.Core;

namespace TangentSpace.Participants;

/// <summary>Exact participant resolution over the identifier kinds that exist today:
/// `did:*` and `tangent:local:*` are stored-key lookups (no participant carries a local key yet),
/// anything else is a handle. The handle rule is the twin of CompanionIdentity.Matches in
/// TangentSpace.Mcp — trimmed, one optional leading @, OrdinalIgnoreCase against the stored
/// handle; DID Ordinal — to be unified with it in refinement 0b. Misses are honest: never fuzzy,
/// and a handle held by more than one participant resolves to nothing.</summary>
public static class ParticipantLookup
{
    public const string LocalPrefix = "tangent:local:";

    /// <summary>Resolves one identifier to its current holder. MatchedForm is the top of the
    /// canonicalization chain — current handle, else DID, else `tangent:local:` — so callers can
    /// redirect perennial or decorated forms to the canonical /u/ address.</summary>
    public static async Task<(Participant Participant, string MatchedForm)?> TryResolveByIdentifier(
        string identifier, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 253) return null;
        using var fresh = EntityContext.NoCache();
        if (identifier.StartsWith("did:", StringComparison.Ordinal) || identifier.StartsWith(LocalPrefix, StringComparison.Ordinal))
        {
            var stored = await Participant.Get(identifier, ct);
            return stored is null ? null : (stored, TopForm(stored));
        }
        var supplied = identifier.Trim();
        if (supplied.StartsWith('@') && supplied.Length > 1) supplied = supplied[1..];
        // The collision check case-folds stored handles as trimmed at comparison time; the scan
        // keeps ambiguity detection honest across case-variant stored forms.
        Participant? matched = null;
        foreach (var candidate in await Participant.All(ct))
        {
            if (candidate.Handle is not { Length: > 0 } handle) continue;
            if (!string.Equals(supplied, handle.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            if (matched is not null) return null;
            matched = candidate;
        }
        return matched is null ? null : (matched, TopForm(matched));
    }

    private static string TopForm(Participant participant)
        => participant.Handle is { Length: > 0 } handle ? handle.Trim() : participant.Id;
}
