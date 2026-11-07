using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Sorting;
using Koan.Jobs;

namespace Tangent.Connections;

/// <summary>Wakes the Connections that publish once the log moved: each one's job is submitted, which coalesces onto a
/// job already waiting, and reads the log from its own cursor. They are read <see cref="ReadSize"/> at a time, in Id
/// order, each read continuing by seek from the last Id the one before read. Nothing runs while no Connection publishes:
/// a wake that finds none is believed until a Connection changes (<see cref="Changed"/>), one changed while it read
/// included. The
/// submission runs beside the write that moved the log, after its commit and never inside its hold, and a failure is
/// logged: the next write wakes them again.</summary>
public sealed class Nudge(ILogger<Nudge> logger)
{
    /// <summary>The longest a wake takes before it is given up.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    /// <summary>The most Connections one read takes.</summary>
    public const int ReadSize = 100;

    /// <summary>How many times a Connection was made, changed or removed.</summary>
    private int _changes;

    /// <summary>The count of <see cref="_changes"/> at which a wake found no Connection publishing; -1 while one may.</summary>
    private volatile int _noneAt = -1;

    /// <summary>A Connection was made, changed or removed: the next wake reads which ones publish.</summary>
    public void Changed() => Interlocked.Increment(ref _changes);

    /// <summary>The log moved: run once its write committed, outside its hold.</summary>
    public Task Moved() => _noneAt == Volatile.Read(ref _changes) ? Task.CompletedTask : Wake();

    /// <summary>Submits the job of every Connection that publishes, or of <paramref name="only"/>'s alone, one keyed read.</summary>
    public async Task Wake(string? only = null)
    {
        using var bounded = new CancellationTokenSource(Bound);
        try
        {
            if (only is not null)
            {
                if (await Connection.Get(only, bounded.Token) is { } one && one.Publishes()) await one.Job.Submit(ct: bounded.Token);
                return;
            }

            var changes = Volatile.Read(ref _changes);
            var any = false;
            string? after = null;
            while (true)
            {
                var query = new QueryDefinition
                {
                    Filter = after is null ? null : Filter.On(FieldPath.Of(nameof(Connection.Id)), FilterOperator.Gt, FilterValue.Of(after)),
                    Page = 1,
                    PageSize = ReadSize
                }.WithSort<Connection>(sort => sort.OrderBy(connection => connection.Id));
                var rows = await Connection.Query(connection => connection.State == ConnectionState.Publishing && !connection.NeedsGrant, query, bounded.Token);
                foreach (var connection in rows)
                {
                    any = true;
                    await connection.Job.Submit(ct: bounded.Token);
                }

                if (rows.Count < ReadSize) break;
                after = rows[^1].Id;
            }

            _noneAt = any ? -1 : changes;
        }
        catch (Exception failure)
        {
            logger.LogWarning(failure, "The Connections were not woken");
        }
    }
}
