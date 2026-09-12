using Koan.Data.Core;

namespace TangentSpace.Participants;

/// <summary>Exact participant resolution over the identity collection: `did:*` and
/// `tangent:local:*` are exact identity-value lookups, anything else is a handle matched
/// against identity-entry labels. The label rule is the twin of CompanionIdentity.Matches in
/// TangentSpace.Mcp — trimmed, one optional leading @, OrdinalIgnoreCase against the stored
/// label; identity values compare Ordinal. Misses are honest: never fuzzy, and a label held
/// by more than one participant resolves to nothing.</summary>
public static class ParticipantLookup
{
    public const string LocalPrefix = "tangent:local:";

    /// <summary>Resolves one identifier to its current holder. MatchedForm is the top of the
    /// canonicalization chain — current label, else the perennial identity value — so callers
    /// can redirect decorated forms to the canonical /u/ address.</summary>
    public static async Task<(Participant Participant, string MatchedForm)?> TryResolveByIdentifier(
        string identifier, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 253) return null;
        Participant? participant;
        if (Participant.IsValidId(identifier))
        {
            // A raw participant id resolves to itself: callers that already resolved an external
            // identifier may pass the id back through this one seam.
            participant = await Participant.Get(identifier, ct);
        }
        else if (identifier.StartsWith("did:", StringComparison.Ordinal))
        {
            var identity = await ParticipantIdentity.Get(ParticipantIdentity.AtprotoKey(identifier), ct);
            if (identity is null) return null;
            participant = await Participant.Get(identity.ParticipantId, ct);
        }
        else if (identifier.StartsWith(LocalPrefix, StringComparison.Ordinal)
            && Participant.IsValidId(identifier[LocalPrefix.Length..]))
        {
            // The internal identity is derived; the row only exists for a minted participant.
            participant = await Participant.Get(identifier[LocalPrefix.Length..], ct);
            if (participant is null) return null;
            var internalIdentity = await ParticipantIdentity.Get(ParticipantIdentity.InternalKey(participant.Id), ct);
            if (internalIdentity is null || internalIdentity.Value != identifier) return null;
        }
        else
        {
            var supplied = identifier.Trim();
            if (supplied.StartsWith('@') && supplied.Length > 1) supplied = supplied[1..];
            // The collision check case-folds stored labels as trimmed at comparison time; the scan
            // keeps ambiguity detection honest across case-variant stored forms.
            string? holder = null;
            foreach (var entry in await ParticipantIdentity.Query(value => value.Label != null, ct))
            {
                if (entry.Label is not { Length: > 0 } label) continue;
                if (!string.Equals(supplied, label.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (holder is not null && holder != entry.ParticipantId) return null;
                holder = entry.ParticipantId;
            }
            participant = holder is null ? null : await Participant.Get(holder, ct);
        }
        return participant is null ? null : (participant, await TopForm(participant, ct));
    }

    private static async Task<string> TopForm(Participant participant, CancellationToken ct)
    {
        // The canonicalization chain (decision D1): current label, else the atproto DID,
        // else the derived internal DID.
        string? atprotoLabel = null, otherLabel = null, atprotoValue = null;
        foreach (var identity in await ParticipantIdentity.Query(value => value.ParticipantId == participant.Id, ct))
        {
            if (identity.Kind == ParticipantIdentity.AtprotoKind) atprotoValue ??= identity.Value;
            if (identity.Label is not { Length: > 0 } label) continue;
            if (identity.Kind == ParticipantIdentity.AtprotoKind) atprotoLabel ??= label;
            else otherLabel ??= label;
        }
        var top = atprotoLabel ?? otherLabel ?? atprotoValue ?? ParticipantIdentity.InternalValue(participant.Id);
        return top.Trim();
    }
}
