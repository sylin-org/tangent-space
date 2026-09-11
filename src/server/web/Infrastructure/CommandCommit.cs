namespace TangentSpace.Infrastructure;

/// <summary>
/// A transport may persist its receipt alongside a local domain mutation. Domain services report
/// their outcome before committing; they know nothing about transport schemas or credentials.
/// The callback must only stage local data in the current transaction, never perform network I/O.
/// </summary>
public static class CommandCommit
{
    private static readonly AsyncLocal<Func<object?, CancellationToken, Task>?> observer = new();

    public static IDisposable Observe(Func<object?, CancellationToken, Task> callback)
    {
        var previous = observer.Value;
        observer.Value = callback;
        return new Restore(previous);
    }

    public static Task Report(object? result, CancellationToken ct)
        => observer.Value?.Invoke(result, ct) ?? Task.CompletedTask;

    private sealed class Restore(Func<object?, CancellationToken, Task>? previous) : IDisposable
    {
        public void Dispose() => observer.Value = previous;
    }
}
