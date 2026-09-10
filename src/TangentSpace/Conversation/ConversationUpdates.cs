namespace TangentSpace.Conversation;

// A wake-up hint for this process. Durable history and current authorization remain authoritative.
public sealed class ConversationUpdates
{
    private readonly object gate = new();
    private readonly Dictionary<string, Signal> rooms = new(StringComparer.Ordinal);

    public Subscription Capture(string room)
    {
        ArgumentException.ThrowIfNullOrEmpty(room);
        lock (gate)
        {
            if (!rooms.TryGetValue(room, out var signal)) rooms.Add(room, signal = new Signal());
            signal.Subscribers++;
            return new Subscription(signal.Changed.Task, () => Release(room, signal));
        }
    }

    public void Pulse(string room)
    {
        Signal? signal;
        lock (gate) rooms.Remove(room, out signal);
        // New subscribers capture a new generation; previously captured tasks retain this pulse.
        signal?.Changed.TrySetResult();
    }

    private void Release(string room, Signal signal)
    {
        lock (gate)
        {
            if (rooms.TryGetValue(room, out var current) && ReferenceEquals(current, signal)
                && --signal.Subscribers == 0) rooms.Remove(room);
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
