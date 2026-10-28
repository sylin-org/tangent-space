using Koan.Data.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Sorting;

namespace Tangent.Identity;

/// <summary>Exact participant resolution over the identity collection: `did:*` and
/// `tangent:local:*` are exact identity-value lookups, anything else is a handle matched
/// against identity-entry labels: trimmed, one optional leading @, OrdinalIgnoreCase against the
/// stored label; identity values compare Ordinal. Misses are honest: never fuzzy, and a label held
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
            string? holder = null;
            foreach (var entry in await Matching(Filter.Eq(nameof(ParticipantIdentity.LabelKey), supplied.ToLowerInvariant()), ct))
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

    /// <summary>Resolve only mention candidates present in a bounded Post, through native indexed filters.
    /// Fetch all matching identities before deciding ambiguity; an incomplete result never grants a match.</summary>
    internal static async Task<IReadOnlyList<ParticipantIdentity>> MentionIdentities(
        IEnumerable<string> handles, IEnumerable<string> dids, CancellationToken ct)
    {
        var found = new Dictionary<string, ParticipantIdentity>(StringComparer.Ordinal);
        foreach (var keys in handles.Select(value => value.Trim().ToLowerInvariant()).Distinct().Chunk(32))
            foreach (var entry in await Matching(Filter.In(nameof(ParticipantIdentity.LabelKey), keys), ct))
                found[entry.Id] = entry;
        foreach (var values in dids.Distinct(StringComparer.Ordinal).Chunk(32))
            foreach (var entry in await Matching(Filter.In(nameof(ParticipantIdentity.Value), values), ct))
                found[entry.Id] = entry;
        // A handle may live on another identity entry than its holder's perennial DID.
        foreach (var holders in found.Values.Select(entry => entry.ParticipantId).Distinct(StringComparer.Ordinal).ToArray().Chunk(32))
            foreach (var entry in await Matching(Filter.In(nameof(ParticipantIdentity.ParticipantId), holders), ct))
                found[entry.Id] = entry;
        return found.Values.ToArray();
    }

    private static async Task<IReadOnlyList<ParticipantIdentity>> Matching(Filter filter, CancellationToken ct)
    {
        const int maximum = 256;
        var query = QueryDefinition.All.Where(filter) with
        {
            Page = 1, PageSize = maximum + 1,
            Sort = SortSpecParser.ParseStrict<ParticipantIdentity>(nameof(ParticipantIdentity.Id))
        };
        var entries = await ParticipantIdentity.All(query, ct);
        if (entries.Count > maximum)
            throw new ArgumentException("Too many matching identity entries; narrow the participant lookup.");
        return entries;
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
