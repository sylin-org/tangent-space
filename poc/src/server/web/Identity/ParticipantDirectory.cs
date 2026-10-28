using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;

namespace Tangent.Identity;

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
    public async Task<string?> LabelOf(string participantId, CancellationToken ct, bool resolveMissing = true)
        => (await LabelsFor([participantId], ct, resolveMissing)).GetValueOrDefault(participantId);

    internal const int LabelInputLimit = 4096;
    internal const int LabelParticipantIdLimit = 128;
    internal const int LabelQueryParticipants = 64;
    internal const int LabelIdentityPageSize = 128;
    internal const int LabelIdentityLimit = 32768;
    internal const int LabelResolveConcurrency = 6;

    /// <summary>Batch label resolution for bylines, digests and mentionables. Missing labels
    /// are absent, never evidence of identity or authority. Admission counts raw input items
    /// (including duplicates/nulls); excessive input or matching identity rows throws rather
    /// than returning a silently incomplete label map. Callers with larger candidate sets
    /// must window that set upstream. Label spelling, Unicode and blank-label behavior are
    /// unchanged; equal-kind identities are selected by ordinal Id, not provider row order.</summary>
    public async Task<IReadOnlyDictionary<string, string>> LabelsFor(IEnumerable<string> participantIds, CancellationToken ct, bool resolveMissing = true)
    {
        ArgumentNullException.ThrowIfNull(participantIds);
        ct.ThrowIfCancellationRequested();
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        var inputCount = 0;
        foreach (var id in participantIds)
        {
            ct.ThrowIfCancellationRequested();
            if (++inputCount > LabelInputLimit)
                throw new ArgumentException($"Label lookup accepts at most {LabelInputLimit} input items; window the participant set first.", nameof(participantIds));
            if (id is null) continue;
            if (id.Length > LabelParticipantIdLimit)
                throw new ArgumentException($"A label lookup participant id may not exceed {LabelParticipantIdLimit} characters.", nameof(participantIds));
            wanted.Add(id);
        }
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var identityCount = 0;
        foreach (var ids in wanted.Chunk(LabelQueryParticipants))
        {
            var candidates = ids.ToDictionary(id => id, _ => new LabelCandidates(), StringComparer.Ordinal);
            // Declare native IN explicitly. Array.Contains currently lowers to a CLR
            // residual on the pinned compiler/runtime, which would scan unrelated rows.
            var scope = Filter.All(Filter.In(nameof(ParticipantIdentity.ParticipantId), ids),
                LinqFilterCompiler.Compile<ParticipantIdentity>(identity => identity.Label != null
                    || identity.Kind == ParticipantIdentity.AtprotoKind));
            string? after = null;
            while (true)
            {
                var filter = after is null ? scope : Filter.All(scope,
                    Filter.On(FieldPath.Of(nameof(ParticipantIdentity.Id)), FilterOperator.Gt, FilterValue.Of(after)));
                var query = QueryDefinition.All.Where(filter); // QueryStream supplies the total Id order.
                var pageCount = 0;
                // Read only the first count-free provider page, then use its Id as the next
                // predicate edge. Never continue Koan's OFFSET stream across mutable pages.
                using (EntityContext.NoCache())
                {
                    await foreach (var identity in ParticipantIdentity.QueryStream(query, LabelIdentityPageSize, ct))
                    {
                        if (++identityCount > LabelIdentityLimit)
                            throw new InvalidOperationException($"Label lookup exceeded {LabelIdentityLimit} matching identities; narrow the participant set.");
                        candidates[identity.ParticipantId].Consider(identity);
                        after = identity.Id;
                        if (++pageCount == LabelIdentityPageSize) break;
                    }
                }
                if (pageCount < LabelIdentityPageSize) break;
            }
            // Only two winners per requested participant survive a provider page. Resolve
            // at most six at once, matching the handle source's width without allocating
            // an input-wide task/waiter fan-out or serializing every network timeout.
            foreach (var group in candidates.Chunk(LabelResolveConcurrency))
            {
                ct.ThrowIfCancellationRequested();
                var labels = await Task.WhenAll(group.Select(async pair =>
                {
                    ct.ThrowIfCancellationRequested();
                    var label = pair.Value.Atproto?.Label;
                    if (resolveMissing && string.IsNullOrWhiteSpace(label) && pair.Value.Atproto is { } atproto)
                        label = await handles.HandleOf(atproto.Value, ct);
                    return (pair.Key, Label: label ?? pair.Value.Fallback?.Label);
                }));
                foreach (var (participantId, label) in labels)
                    if (label is { Length: > 0 }) result[participantId] = label;
            }
        }
        return result;
    }

    private sealed class LabelCandidates
    {
        public ParticipantIdentity? Atproto { get; private set; }
        public ParticipantIdentity? Fallback { get; private set; }

        public void Consider(ParticipantIdentity identity)
        {
            if (identity.Kind == ParticipantIdentity.AtprotoKind
                && (Atproto is null || StringComparer.Ordinal.Compare(identity.Id, Atproto.Id) < 0))
                Atproto = identity;
            if (identity.Label is not { Length: > 0 }) return;
            var kindOrder = Fallback is null ? -1 : StringComparer.Ordinal.Compare(identity.Kind, Fallback.Kind);
            if (kindOrder < 0 || kindOrder == 0 && StringComparer.Ordinal.Compare(identity.Id, Fallback!.Id) < 0)
                Fallback = identity;
        }
    }

    /// <summary>All identity entries of one participant, ordered best-first for display.</summary>
    public async Task<IReadOnlyList<ParticipantIdentity>> IdentitiesOf(string participantId, CancellationToken ct)
        => (await ParticipantIdentity.Query(value => value.ParticipantId == participantId, ct))
            .OrderBy(identity => identity.Kind != ParticipantIdentity.AtprotoKind)
            .ThenBy(identity => identity.Kind, StringComparer.Ordinal)
            .ToArray();
}
