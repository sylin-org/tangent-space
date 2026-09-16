using System.Text.Json;
using CarpaNet.OAuth.Storage;
using Microsoft.AspNetCore.DataProtection;

// Single-process disposable probe storage. SDK state is consumed atomically under the lock.
// Production multi-instance storage and OS/service-key custody belong to the integration story.
public sealed class ProtectedStore : IOAuthStateStore, IOAuthSessionStore
{
    private readonly object gate = new();
    private readonly IDataProtector protector;
    private readonly string file;
    private StoreData data;

    public ProtectedStore(IDataProtectionProvider provider, string directory)
    {
        Directory.CreateDirectory(directory);
        protector = provider.CreateProtector("Community.Tangent.AtprotoClientProbe.State.v1");
        file = Path.Combine(directory, "sessions.protected");
        data = File.Exists(file)
            ? JsonSerializer.Deserialize<StoreData>(protector.Unprotect(File.ReadAllText(file)))!
            : new StoreData();
    }

    private void Save()
    {
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, protector.Protect(JsonSerializer.Serialize(data)));
        File.Move(temporary, file, true);
    }

    public Task StoreAsync(string state, OAuthStateData value, CancellationToken cancellationToken = default)
    {
        lock (gate) { data.States[state] = value; Save(); }
        return Task.CompletedTask;
    }

    public OAuthStateData? Peek(string state)
    {
        lock (gate) return data.States.GetValueOrDefault(state) is { } value && value.ExpiresAt > DateTimeOffset.UtcNow ? value : null;
    }

    public Task<OAuthStateData?> ConsumeAsync(string state, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var value = data.States.GetValueOrDefault(state);
            data.States.Remove(state);
            Save();
            return Task.FromResult(value is not null && value.ExpiresAt > DateTimeOffset.UtcNow ? value : null);
        }
    }

    public Task StoreAsync(string sub, OAuthSessionData value, CancellationToken cancellationToken = default)
    {
        if (sub != value.TokenSet.Sub) throw new ProbeRejected("stored_token_subject_mismatch");
        lock (gate) { data.Sessions[sub] = value; Save(); }
        return Task.CompletedTask;
    }

    public void ExpireForProbe(string did)
    {
        lock (gate)
        {
            if (!data.Sessions.TryGetValue(did, out var session)) throw new ProbeRejected("session_required");
            session.TokenSet.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            Save();
        }
    }

    public Task<OAuthSessionData?> GetAsync(string sub, CancellationToken cancellationToken = default)
    {
        lock (gate) return Task.FromResult(data.Sessions.GetValueOrDefault(sub));
    }

    public Task DeleteAsync(string sub, CancellationToken cancellationToken = default)
    {
        lock (gate) { data.Sessions.Remove(sub); Save(); }
        return Task.CompletedTask;
    }

    public object[] Describe()
    {
        lock (gate) return data.Sessions.Select(x => (object)new
        {
            did = x.Key, pds = x.Value.TokenSet.Audience, issuer = x.Value.TokenSet.Issuer,
            expiresAt = x.Value.TokenSet.ExpiresAt, scope = x.Value.Scope,
            refreshAvailable = !string.IsNullOrEmpty(x.Value.TokenSet.RefreshToken)
        }).ToArray();
    }

    public sealed class StoreData
    {
        public Dictionary<string, OAuthStateData> States { get; set; } = new();
        public Dictionary<string, OAuthSessionData> Sessions { get; set; } = new();
    }
}
