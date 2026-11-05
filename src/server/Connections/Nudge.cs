using Koan.Jobs;

namespace Tangent.Connections;

/// <summary>Wakes the Connections that publish once the log moved: each one's job is submitted, which coalesces onto a
/// job already waiting, and reads the log from its own cursor. Nothing runs while no Connection publishes: which ones do
/// is read once and again only after a Connection changed (<see cref="Changed"/>). The submission runs beside the
/// write that moved the log, after its commit and never inside its hold, and a failure is logged: the next write wakes
/// them again.</summary>
public sealed class Nudge(ILogger<Nudge> logger)
{
    /// <summary>The longest a wake takes before it is given up.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private volatile IReadOnlyList<string>? _publishing;

    /// <summary>A Connection was made, changed or removed: the next wake reads which ones publish.</summary>
    public void Changed() => _publishing = null;

    /// <summary>The log moved: run once its write committed, outside its hold.</summary>
    public Task Moved() => _publishing is { Count: 0 } ? Task.CompletedTask : Wake();

    /// <summary>Submits the job of every Connection that publishes, or of <paramref name="only"/>.</summary>
    public async Task Wake(string? only = null)
    {
        using var bounded = new CancellationTokenSource(Bound);
        try
        {
            var publishing = _publishing ??= (await Connection.Query(connection => connection.State == ConnectionState.Publishing, bounded.Token))
                .Where(connection => !connection.NeedsGrant).Select(connection => connection.Id).ToList();
            foreach (var id in only is null ? publishing : publishing.Where(id => id == only))
                if (await Connection.Get(id, bounded.Token) is { } connection) await connection.Job.Submit(ct: bounded.Token);
        }
        catch (Exception failure)
        {
            logger.LogWarning(failure, "The Connections were not woken");
        }
    }
}
