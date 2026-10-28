namespace Tangent.Activity;

/// <summary>Token-only sessions (docs/DECISIONS.md, 11 September 2026): live SSE connections
/// register under their session claim, so a replacing sign-in can push an identity_changed
/// event to exactly the session it displaced. In-memory on purpose — the notification only
/// needs to reach connections still held by this process, and connections already
/// authenticated when they opened.</summary>
public sealed class LiveSessions
{
    /// <summary>Who the browser now is, delivered on the replaced session's connections.</summary>
    public sealed record Identity(string ParticipantRef, string BestLabel, string Reason);

    private readonly object gate = new();
    private readonly Dictionary<string, List<TaskCompletionSource<Identity>>> sessions = new(StringComparer.Ordinal);

    /// <summary>Register a connection's wake signal for its session; completing the signal
    /// with an <see cref="Identity"/> carries the identity_changed payload.</summary>
    public IDisposable Register(string session, TaskCompletionSource<Identity> signal)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(session, out var list)) sessions[session] = list = [];
            list.Add(signal);
        }
        return new Registration(this, session, signal);
    }

    /// <summary>A sign-in replaced this session: wake its live connections with who the
    /// browser now is. Unknown sessions are a no-op (nothing live to tell).</summary>
    public void Replaced(string session, Identity identity)
    {
        List<TaskCompletionSource<Identity>>? toWake = null;
        lock (gate) sessions.Remove(session, out toWake);
        if (toWake is null) return;
        foreach (var signal in toWake) signal.TrySetResult(identity);
    }

    private sealed class Registration(LiveSessions owner, string session, TaskCompletionSource<Identity> signal) : IDisposable
    {
        public void Dispose()
        {
            lock (owner.gate)
            {
                if (!owner.sessions.TryGetValue(session, out var list)) return;
                list.Remove(signal);
                if (list.Count == 0) owner.sessions.Remove(session);
            }
        }
    }
}
