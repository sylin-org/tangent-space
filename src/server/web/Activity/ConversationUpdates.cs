namespace Tangent.Activity;

// A wake-up hint for this process. Durable history and current authorization remain authoritative.
public sealed class ConversationUpdates
{
    private readonly object gate = new();
    private readonly Dictionary<string, Signal> topics = new(StringComparer.Ordinal);

    public Subscription Capture(string topic)
    {
        ArgumentException.ThrowIfNullOrEmpty(topic);
        lock (gate)
        {
            if (!topics.TryGetValue(topic, out var signal)) topics.Add(topic, signal = new Signal());
            signal.Subscribers++;
            return new Subscription(signal.Changed.Task, () => Release(topic, signal));
        }
    }

    public void Pulse(string topic)
    {
        Signal? signal;
        lock (gate) topics.Remove(topic, out signal);
        // New subscribers capture a new generation; previously captured tasks retain this pulse.
        signal?.Changed.TrySetResult();
    }

    private void Release(string topic, Signal signal)
    {
        lock (gate)
        {
            if (topics.TryGetValue(topic, out var current) && ReferenceEquals(current, signal)
                && --signal.Subscribers == 0) topics.Remove(topic);
        }
    }

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
