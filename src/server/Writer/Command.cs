namespace Tangent.Writer;

/// <summary>One write, as a value: what the writer runs inside one transaction. The command carries every Id it
/// creates; the writer mints none. It runs against the store and nothing else: no network, no lock.</summary>
public interface ICommand<TResult>
{
    /// <summary>Runs inside the writer's transaction and returns the outcome. A <see cref="Refusal"/> an entity throws
    /// reaches the sender as the refused outcome; any other throw is the failure. Nothing it saves is durable until
    /// the writer commits, and nothing it saves is visible to it before then either.</summary>
    Task<Outcome<TResult>> Run(Sequences sequences, CancellationToken ct);
}

/// <summary>The sequences one command takes from the writer's high-water marks, held until its commit. Each take is
/// one higher than the last; a command that does not commit gives them back, so a mark's committed sequence has no
/// gap.</summary>
public sealed class Sequences
{
    private readonly Dictionary<string, long> _committed;
    private readonly Dictionary<string, long> _taken = new(StringComparer.Ordinal);

    internal Sequences(Dictionary<string, long> committed) => _committed = committed;

    /// <summary>The next sequence of the named mark.</summary>
    public long Next(string mark)
    {
        if (!_taken.TryGetValue(mark, out var last) && !_committed.TryGetValue(mark, out last))
            throw new InvalidOperationException($"No high-water mark named '{mark}' is registered with the writer.");
        var next = checked(last + 1);
        _taken[mark] = next;
        return next;
    }

    /// <summary>The last sequence taken from each mark this command touched.</summary>
    public IReadOnlyDictionary<string, long> Taken => _taken;

    internal void Keep()
    {
        foreach (var (mark, last) in _taken) _committed[mark] = last;
    }
}

/// <summary>A sequence the writer continues: its name, and the one bounded query that reads the highest value the
/// store holds. The package that owns the numbered rows registers it; the writer loads it once at start and never
/// scans for it again.</summary>
public sealed record HighWaterMark(string Name, Func<CancellationToken, Task<long>> Load);
