namespace Tangent.Application;

/// <summary>One non-reentrant host gate for arrival, policy changes, and local source acceptance.</summary>
public sealed class PolicyGate : IDisposable
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    internal Task Enter(CancellationToken ct) => semaphore.WaitAsync(ct);
    internal void Exit() => semaphore.Release();
    public void Dispose() => semaphore.Dispose();
}
