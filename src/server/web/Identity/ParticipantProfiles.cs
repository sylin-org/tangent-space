using System.Collections.Concurrent;
using System.Threading.Channels;
using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Tangent.Identity;

public sealed record ParticipantProfile(string Did, string? Handle, string? DisplayName,
    string? Description, string? Avatar, string Status);

/// <summary>Rebuildable public decoration, never identity or permission evidence.
/// Reads are cache-backed: the versioned avatar URL and the snapshot ride Koan.Cache.</summary>
[Cacheable(300)]
public sealed class ParticipantProfileSnapshot : Entity<ParticipantProfileSnapshot>
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? AvatarFile { get; set; }
    public string? AvatarCid { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset RetryAfter { get; set; }

    public ParticipantProfile Present(string? handle = null) => new(Id, handle, DisplayName, Description,
        AvatarFile is null ? null : "/api/profile-cache/avatar?did=" + Uri.EscapeDataString(Id) + "&v=" + AvatarFile
            + "&r=" + CapturedAt.ToUnixTimeMilliseconds(),
        CapturedAt == default ? "loading" : "loaded");
}

/// <summary>Local reads and capture requests. Only the capture worker publishes snapshots.</summary>
public sealed class ParticipantProfiles(ParticipantDirectory directory, TimeProvider clock)
{
    private readonly Channel<string> captures = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        { SingleReader = false, FullMode = BoundedChannelFullMode.Wait });
    private readonly ConcurrentDictionary<string, byte> pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, Action<string>> consumers = new();
    internal ChannelReader<string> Requests => captures.Reader;

    public async Task<ParticipantProfile> Read(string participantId, CancellationToken ct)
    {
        // Identity resolution stays outside the cache tier; the snapshot read must be
        // OUTSIDE the NoCache scope or the read-through never fires.
        string? did, handle;
        using (EntityContext.NoCache())
        {
            did = await directory.AtprotoDidOf(participantId, ct);
            handle = await directory.LabelOf(participantId, ct, resolveMissing: false);
        }
        if (did is null) return new(ParticipantIdentity.InternalValue(participantId), handle, handle, null, null, "local");
        var snapshot = await Cached(did, ct);
        if (snapshot.RetryAfter <= clock.GetUtcNow()) Request(did);
        return snapshot.Present(handle);
    }

    public async Task<ParticipantProfileSnapshot> Cached(string did, CancellationToken ct)
        => await ParticipantProfileSnapshot.Get(did, ct) ?? new() { Id = did };

    public void Request(string did)
    {
        if (!CarpaNet.Identity.IdentityResolver.IsValidDid(did) || !pending.TryAdd(did, 0)) return;
        if (!captures.Writer.TryWrite(did)) pending.TryRemove(did, out _);
    }

    internal void Complete(string did) => pending.TryRemove(did, out _);

    internal async Task Store(ParticipantProfileSnapshot snapshot, bool changed, CancellationToken ct)
    {
        // Save writes through: the cache holds the fresh snapshot for the next reader.
        await snapshot.Save(ct);
        if (changed) foreach (var notify in consumers.Values) notify(snapshot.Id);
    }

    // The notification carries a DID only; each consumer decides what it needs to read.
    public IDisposable Subscribe(Action<string> notify)
    {
        var key = Guid.NewGuid(); consumers[key] = notify;
        return new Subscription(() => consumers.TryRemove(key, out _));
    }
    private sealed class Subscription(Action remove) : IDisposable { public void Dispose() => remove(); }
}
