namespace TangentSpace.Activity;

// A process-local hint only. ActivityJournal is the authoritative replay source after restarts.
internal sealed class ActivityUpdates
{
    private readonly object gate = new();
    private Signal? current;

    public Subscription Capture()
    {
        lock (gate)
        {
            current ??= new Signal();
            var signal = current;
            signal.Subscribers++;
            return new Subscription(signal.Changed.Task, () => Release(signal));
        }
    }

    public void Pulse()
    {
        Signal? signal;
        lock (gate) { signal = current; current = null; }
        signal?.Changed.TrySetResult();
    }

    private void Release(Signal signal)
    {
        lock (gate)
        {
            if (ReferenceEquals(current, signal) && --signal.Subscribers == 0) current = null;
        }
    }

    private sealed class Signal
    {
        public TaskCompletionSource Changed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Subscribers { get; set; }
    }

    public sealed class Subscription(Task changed, Action release) : IDisposable
    {
        private Action? releaseOnce = release;

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct)
        {
            try { await changed.WaitAsync(timeout, ct); return true; }
            catch (TimeoutException) { return false; }
        }

        public void Dispose() => Interlocked.Exchange(ref releaseOnce, null)?.Invoke();
    }
}
