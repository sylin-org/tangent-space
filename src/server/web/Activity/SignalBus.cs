namespace Tangent.Activity;

/// <summary>
/// The host's one live bus: a signal means committed state changed. Subscribers capture before
/// reading and wake to re-derive from current authority; a signal carries no payload, so a missed
/// or duplicate one costs one poll and never a wrong answer. Durability lives in the journal and
/// the digest's cursors, never here. A null or empty topic is the server-wide signal; otherwise
/// the wake is scoped to one Topic.
/// </summary>
public sealed class SignalBus
{
    private readonly object gate = new();
    private readonly Dictionary<string, Signal> topics = new(StringComparer.Ordinal);

    public Subscription Capture(string? topic)
    {
        lock (gate)
        {
            var key = Normalize(topic);
            if (!topics.TryGetValue(key, out var signal)) topics.Add(key, signal = new Signal());
            signal.Subscribers++;
            return new Subscription(signal.Changed.Task, () => Release(key, signal));
        }
    }

    public void Pulse(string? topic = null)
    {
        Signal? signal;
        lock (gate) topics.Remove(Normalize(topic), out signal);
        // New subscribers capture a new generation; previously captured tasks retain this pulse.
        signal?.Changed.TrySetResult();
    }

    private void Release(string key, Signal signal)
    {
        lock (gate)
        {
            if (topics.TryGetValue(key, out var current) && ReferenceEquals(current, signal)
                && --signal.Subscribers == 0) topics.Remove(key);
        }
    }

    private static string Normalize(string? topic) => string.IsNullOrWhiteSpace(topic) ? "" : topic;

    private sealed class Signal
    {
        public readonly TaskCompletionSource Changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Subscribers;
    }

    public sealed class Subscription(Task changed, Action release) : IDisposable
    {
        private Action? releaseOnce = release;

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            try { await changed.WaitAsync(timeout, ct); return true; }
            catch (TimeoutException) { return false; }
        }

        public void Dispose() => Interlocked.Exchange(ref releaseOnce, null)?.Invoke();
    }
}
