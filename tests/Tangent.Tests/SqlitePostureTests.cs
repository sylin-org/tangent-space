using System.Collections.Concurrent;
using System.Diagnostics;
using Koan.Data.Core;
using Koan.Data.Core.Model;
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

/// <summary>Koan's SQLite posture at the pinned revision, in the Tangent Space's own database: concurrent
/// single-row saves, what <see cref="EntityContext.Transaction"/> does inside a <see cref="BackgroundService"/>,
/// and which transaction the driver begins by default. Each fact starts its own Tangent Space.</summary>
public sealed class SqlitePostureTests(ITestOutputHelper output)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_tasks_each_save_one_hundred_rows_without_a_failure()
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
    }

    [Fact]
    public async Task A_throw_between_two_saves_in_one_transaction_persists_neither()
    {
        var probe = new TransactionProbe(ThrowBetween: true);
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            services: services => services.AddSingleton(probe).AddHostedService<TransactionWorker>(),
            ct: Cancellation);

        var outcome = await probe.Outcome.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        output.WriteLine(outcome.ToString());

        var thrown = Assert.IsType<InvalidOperationException>(outcome.Thrown);
        Assert.Equal(TransactionWorker.ThrownBetween, thrown.Message);
        Assert.False(outcome.FirstPersisted);
        Assert.False(outcome.SecondPersisted);
    }

    [Fact]
    public async Task Two_saves_in_one_transaction_both_persist_on_commit()
    {
        var probe = new TransactionProbe(ThrowBetween: false);
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            services: services => services.AddSingleton(probe).AddHostedService<TransactionWorker>(),
            ct: Cancellation);

        var outcome = await probe.Outcome.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        output.WriteLine(outcome.ToString());

        Assert.Null(outcome.Thrown);
        Assert.True(outcome.FirstPersisted);
        Assert.True(outcome.SecondPersisted);
    }

    [Fact]
    public async Task A_koan_transaction_writes_nothing_and_holds_no_sqlite_lock_before_commit()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await new ProbeRow { Id = "seed" }.Save(Cancellation);

        using (EntityContext.Transaction("probe"))
        {
            await new ProbeRow { Id = "tracked" }.Save(Cancellation);
            Assert.Null(await ProbeRow.Get("tracked", Cancellation));

            await using var other = Connect(tangentSpace.DatabasePath, "Default Timeout=1");
            await other.OpenAsync(Cancellation);
            await using (var immediate = other.BeginTransaction(deferred: false))
            {
                await immediate.CommitAsync(Cancellation);
            }
            output.WriteLine("BEGIN IMMEDIATE on another connection succeeded while EntityContext.Transaction held a tracked save.");

            await EntityContext.Commit(Cancellation);
        }

        Assert.NotNull(await ProbeRow.Get("tracked", Cancellation));
    }

    [Fact]
    public async Task The_driver_default_transaction_is_immediate()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await new ProbeRow { Id = "seed" }.Save(Cancellation);

        await using var first = Connect(tangentSpace.DatabasePath);
        await first.OpenAsync(Cancellation);
        await using var held = await first.BeginTransactionAsync(Cancellation);

        await using var second = Connect(tangentSpace.DatabasePath, "Default Timeout=1");
        await second.OpenAsync(Cancellation);
        var clock = Stopwatch.StartNew();
        var locked = Assert.Throws<SqliteException>(() => second.BeginTransaction(deferred: false));
        clock.Stop();
        output.WriteLine($"second BEGIN IMMEDIATE failed after {clock.ElapsedMilliseconds}ms: {locked.Message}");
    }

    /// <summary>A connection of the test's own, outside the driver's pool, so it holds no file once disposed.</summary>
    private static SqliteConnection Connect(string databasePath, string more = "")
        => new($"Data Source={databasePath};Pooling=False;{more}");

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

public sealed record TransactionOutcome(Exception? Thrown, bool FirstPersisted, bool SecondPersisted);

/// <summary>The writer's shape: a background loop that saves twice inside one Koan transaction.</summary>
public sealed class TransactionWorker(TransactionProbe probe) : BackgroundService
{
    public const string ThrownBetween = "thrown between the two saves";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Exception? thrown = null;
        try
        {
            using (EntityContext.Transaction("writer"))
            {
                await new ProbeRow { Id = "first" }.Save(ct);
                if (probe.ThrowBetween) throw new InvalidOperationException(ThrownBetween);
                await new ProbeRow { Id = "second" }.Save(ct);
                await EntityContext.Commit(ct);
            }
        }
        catch (Exception failure)
        {
            thrown = failure;
        }

        probe.Outcome.SetResult(new TransactionOutcome(
            thrown,
            await ProbeRow.Get("first", ct) is not null,
            await ProbeRow.Get("second", ct) is not null));
    }
}
