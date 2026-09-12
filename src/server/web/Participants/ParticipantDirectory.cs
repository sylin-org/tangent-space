using Koan.Data.Core;

namespace TangentSpace.Participants;

/// <summary>The one seam between external identifiers and participants. Every DID/identifier
/// lookup, display label and canonicalization goes through this directory; nothing else scans
/// the identity collection for authority. Display order is atproto label &gt; any other
/// identity label &gt; the derived internal DID.</summary>
public sealed class ParticipantDirectory(TimeProvider clock, IAtprotoHandleSource handles)
{
    /// <summary>The mint discipline: participant creation is serialized process-wide by this
    /// async semaphore so a concurrent sign-in and source ingest for the same foreign DID can
    /// never both pass the lookup and mint duplicate spines. The minted participant and its
    /// identity rows persist inside the semaphore before it is released.</summary>
    private static readonly SemaphoreSlim MintGate = new(1, 1);

    /// <summary>Exact atproto identity lookup; one query. Null when no participant holds the DID.</summary>
    public async Task<Participant?> ByDid(string did, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(did)) return null;
        var identity = await ParticipantIdentity.Get(ParticipantIdentity.AtprotoKey(did), ct);
        return identity is null ? null : await Participant.Get(identity.ParticipantId, ct);
    }

    /// <summary>Exact internal identity lookup; one query.</summary>
    public async Task<Participant?> ByInternal(string participantId, CancellationToken ct)
    {
        if (!Participant.IsValidId(participantId)) return null;
        var identity = await ParticipantIdentity.Get(ParticipantIdentity.InternalKey(participantId), ct);
        return identity is null ? null : await Participant.Get(identity.ParticipantId, ct);
    }

    /// <summary>Resolves one identifier (DID, internal DID, or handle) through
    /// <see cref="ParticipantLookup"/>; delegates the matching rules entirely.</summary>
    public async Task<(Participant Participant, string MatchedForm)?> ByIdentifier(string identifier, CancellationToken ct)
        => await ParticipantLookup.TryResolveByIdentifier(identifier, ct);

    /// <summary>Resolves or mints the participant for a verified atproto DID. The minting path
    /// is for source-ingested foreign authors, configured authorities and invitation targets —
    /// identities that never signed in here but must hold a participant row for durable
    /// references. Both the lookup and the full persistence of the minted participant and its
    /// identity rows run inside <see cref="MintGate"/>; callers never persist the minted rows.</summary>
    public async Task<Participant> EnsureAtproto(string verifiedDid, CancellationToken ct)
    {
        await MintGate.WaitAsync(ct);
        try
        {
            var existing = await ByDid(verifiedDid, ct);
            if (existing is not null) return existing;
            var (participant, identities) = Participant.Enroll(verifiedDid, null, clock.GetUtcNow());
            await participant.Save(ct);
            foreach (var identity in identities) await identity.Save(ct);
            return participant;
        }
        finally { MintGate.Release(); }
    }

    /// <summary>Verified atproto arrival for the sign-in path: a first arrival mints, a return
    /// refreshes the arrival stamp and the atproto label. Both branches persist their rows
    /// inside <see cref="MintGate"/>, so a concurrent source ingest of the same DID resolves
    /// to the same participant instead of racing a second mint.</summary>
    public async Task<Participant> ArriveAtproto(string verifiedDid, string? verifiedHandle, DateTimeOffset now, CancellationToken ct)
    {
        await MintGate.WaitAsync(ct);
        try
        {
            var atproto = await ParticipantIdentity.Get(ParticipantIdentity.AtprotoKey(verifiedDid), ct);
            if (atproto is null)
            {
                var (participant, identities) = Participant.Enroll(verifiedDid, verifiedHandle, now);
                await participant.Save(ct);
                foreach (var identity in identities) await identity.Save(ct);
                return participant;
            }
            var returning = await Participant.Get(atproto.ParticipantId, ct)
                ?? throw new InvalidOperationException("The atproto identity has no participant; creation is its only writer.");
            returning.Return(atproto, now);
            if (!string.IsNullOrWhiteSpace(verifiedHandle)) atproto.Relabel(verifiedHandle);
            await returning.Save(ct);
            await atproto.Save(ct);
            return returning;
        }
        finally { MintGate.Release(); }
    }

    /// <summary>The atproto DID this participant currently holds, or null for internal-only
    /// participants. AtProtocol/* and source-write flows cross the boundary here.</summary>
    public async Task<string?> AtprotoDidOf(string participantId, CancellationToken ct)
    {
        foreach (var identity in await ParticipantIdentity.Query(
                     value => value.ParticipantId == participantId && value.Kind == ParticipantIdentity.AtprotoKind, ct))
            return identity.Value;
        return null;
    }

    /// <summary>The perennial identity value for mentions and facet targets: the atproto DID
    /// when held, else the derived internal DID.</summary>
    public async Task<string> PerennialValue(string participantId, CancellationToken ct)
        => await AtprotoDidOf(participantId, ct) ?? ParticipantIdentity.InternalValue(participantId);

    /// <summary>Display/canonicalization chain: atproto label, else another identity's label,
    /// else the derived internal DID. Never empty for a valid participant id.</summary>
    public async Task<string> BestLabel(string participantId, CancellationToken ct)
        => (await LabelsFor([participantId], ct)).GetValueOrDefault(
               participantId, ParticipantIdentity.InternalValue(participantId));

    /// <summary>The best label when one exists on any identity entry; null when only the derived
    /// internal DID would apply. Arrival/welcome packets use this to keep Did/Handle nullable.</summary>
    public async Task<string?> LabelOf(string participantId, CancellationToken ct)
        => (await LabelsFor([participantId], ct)).GetValueOrDefault(participantId);

    /// <summary>Batch label resolution for bylines, digests and mentionables. A missing
    /// participant id is simply absent from the result.</summary>
    public async Task<IReadOnlyDictionary<string, string>> LabelsFor(IEnumerable<string> participantIds, CancellationToken ct)
    {
        var wanted = participantIds.Where(id => id is not null).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (wanted.Count == 0) return result;
        var byParticipant = new Dictionary<string, List<ParticipantIdentity>>(StringComparer.Ordinal);
        foreach (var identity in await ParticipantIdentity.Query(value => value.Label != null || value.Kind == ParticipantIdentity.AtprotoKind, ct))
        {
            if (!wanted.Contains(identity.ParticipantId)) continue;
            (byParticipant.TryGetValue(identity.ParticipantId, out var list) ? list : byParticipant[identity.ParticipantId] = []).Add(identity);
        }
        var labels = await Task.WhenAll(wanted.Select(async participantId =>
        {
            if (!byParticipant.TryGetValue(participantId, out var identities)) return (participantId, label: (string?)null);
            var atproto = identities.FirstOrDefault(identity => identity.Kind == ParticipantIdentity.AtprotoKind);
            var label = atproto?.Label;
            if (string.IsNullOrWhiteSpace(label) && atproto is not null)
                label = await handles.HandleOf(atproto.Value, ct);
            label ??= identities
                .OrderBy(identity => identity.Kind, StringComparer.Ordinal)
                .FirstOrDefault(identity => identity.Label is { Length: > 0 })?.Label;
            return (participantId, label);
        }));
        foreach (var (participantId, label) in labels)
        {
            if (label is { Length: > 0 }) result[participantId] = label;
        }
        return result;
    }

    /// <summary>All identity entries of one participant, ordered best-first for display.</summary>
    public async Task<IReadOnlyList<ParticipantIdentity>> IdentitiesOf(string participantId, CancellationToken ct)
        => (await ParticipantIdentity.Query(value => value.ParticipantId == participantId, ct))
            .OrderBy(identity => identity.Kind != ParticipantIdentity.AtprotoKind)
            .ThenBy(identity => identity.Kind, StringComparer.Ordinal)
            .ToArray();
}
