using System.Collections.Concurrent;
using System.Diagnostics;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Transactions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Tangent;

/// <summary>A row the posture tests write. It exists only here.</summary>
public sealed class ProbeRow : Entity<ProbeRow>
{
    public string Lane { get; set; } = "";

    public int Number { get; set; }
}

/// <summary>A second entity type the atomic-scope tests save beside <see cref="ProbeRow"/>. It exists only here.</summary>
public sealed class ProbeMark : Entity<ProbeMark>
{
    public string Note { get; set; } = "";
}

/// <summary>Koan's SQLite posture in the Tangent Space's own database: write-ahead logging, the busy timeout, and
/// what a scope opened with <see cref="TransactionGuarantee.Atomic"/> does inside a <see cref="BackgroundService"/>,
/// the writer's shape. Each fact starts its own Tangent Space.</summary>
public sealed class SqlitePostureTests(ITestOutputHelper output)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_tasks_each_save_one_hundred_rows_without_a_failure_in_write_ahead_mode()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var failures = new ConcurrentBag<Exception>();

        async Task SaveLane(string lane)
        {
            for (var number = 0; number < 100; number++)
            {
                try { await new ProbeRow { Lane = lane, Number = number }.Save(Cancellation); }
                catch (Exception failure) { failures.Add(failure); }
            }
        }

        var clock = Stopwatch.StartNew();
        await Task.WhenAll(Task.Run(() => SaveLane("a"), Cancellation), Task.Run(() => SaveLane("b"), Cancellation));
        clock.Stop();

        var rows = await ProbeRow.Count.Exact(Cancellation);
        var (read, write) = Header(tangentSpace.DatabasePath);
        output.WriteLine($"rows={rows} failures={failures.Count} elapsed={clock.ElapsedMilliseconds}ms header[18,19]={read},{write}");
        foreach (var failure in failures) output.WriteLine(failure.ToString());

        Assert.Empty(failures);
        Assert.Equal(200, rows);
        Assert.Equal((2, 2), (read, write));
    }

    [Fact]
    public async Task The_busy_timeout_bounds_a_wait_for_a_lock_another_connection_holds()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Koan:Data:Sqlite:BusyTimeout"] = "00:00:02" },
            ct: Cancellation);
        await new ProbeRow { Id = "seed" }.Save(Cancellation);

        await using var holder = Connect(tangentSpace.DatabasePath);
        await holder.OpenAsync(Cancellation);
        await using var held = holder.BeginTransaction(deferred: false);

        var clock = Stopwatch.StartNew();
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => new ProbeRow { Id = "blocked" }.Save(Cancellation));
        clock.Stop();
        output.WriteLine($"the save failed after {clock.ElapsedMilliseconds}ms: {refused.GetType().Name}: {refused.Message}");

        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task An_atomic_scope_that_throws_between_two_saves_of_two_entity_types_persists_neither()
    {
        var probe = new TransactionProbe(ThrowBetween: true);
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            services: services => services.AddSingleton(probe).AddHostedService<TransactionWorker>(),
            ct: Cancellation);

        var outcome = await probe.Outcome.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        output.WriteLine(outcome.ToString());

        var thrown = Assert.IsType<InvalidOperationException>(outcome.Thrown);
        Assert.Equal(TransactionWorker.ThrownBetween, thrown.Message);
        Assert.False(outcome.RowPersisted);
        Assert.False(outcome.MarkPersisted);
    }

    [Fact]
    public async Task An_atomic_scope_commits_two_saves_of_two_entity_types_together()
    {
        var probe = new TransactionProbe(ThrowBetween: false);
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            services: services => services.AddSingleton(probe).AddHostedService<TransactionWorker>(),
            ct: Cancellation);

        var outcome = await probe.Outcome.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        output.WriteLine(outcome.ToString());

        Assert.Null(outcome.Thrown);
        Assert.False(outcome.RowVisibleBeforeCommit);
        Assert.True(outcome.RowPersisted);
        Assert.True(outcome.MarkPersisted);
    }

    /// <summary>A connection of the test's own, outside the driver's pool, so it holds no file once disposed.</summary>
    private static SqliteConnection Connect(string databasePath) => new($"Data Source={databasePath};Pooling=False");

    /// <summary>Header bytes 18 and 19: the read and write versions, 1 for rollback and 2 for write-ahead.</summary>
    private static (byte Read, byte Write) Header(string databasePath)
    {
        var header = new byte[20];
        using var file = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        file.ReadExactly(header);
        return (header[18], header[19]);
    }
}

public sealed record TransactionProbe(bool ThrowBetween)
{
    public TaskCompletionSource<TransactionOutcome> Outcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed record TransactionOutcome(Exception? Thrown, bool RowVisibleBeforeCommit, bool RowPersisted, bool MarkPersisted);

/// <summary>The writer's shape: a background loop that saves a row and a mark inside one atomic scope.</summary>
public sealed class TransactionWorker(TransactionProbe probe) : BackgroundService
{
    public const string ThrownBetween = "thrown between the two saves";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Exception? thrown = null;
        var rowVisibleBeforeCommit = false;
        try
        {
            using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
            {
                await new ProbeRow { Id = "row" }.Save(ct);
                if (probe.ThrowBetween) throw new InvalidOperationException(ThrownBetween);
                await new ProbeMark { Id = "mark" }.Save(ct);
                rowVisibleBeforeCommit = await ProbeRow.Get("row", ct) is not null;
                await EntityContext.Commit(ct);
            }
        }
        catch (Exception failure)
        {
            thrown = failure;
        }

        probe.Outcome.SetResult(new TransactionOutcome(
            thrown,
            rowVisibleBeforeCommit,
            await ProbeRow.Get("row", ct) is not null,
            await ProbeMark.Get("mark", ct) is not null));
    }
}
