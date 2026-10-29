using System.Collections.Concurrent;
using System.Diagnostics;
using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Testing.Integration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Tangent.SqlitePosture;

/// <summary>A row the probe writes. It exists only here.</summary>
public sealed class ProbeRow : Entity<ProbeRow>
{
    public string Lane { get; set; } = "";

    public int Number { get; set; }
}

/// <summary>Koan's SQLite posture at the pinned revision: concurrent single-row saves, what
/// <see cref="EntityContext.Transaction"/> does inside a <see cref="BackgroundService"/>, and
/// which transaction the driver begins by default. Each fact boots its own host over its own folder.</summary>
public sealed class SqlitePostureProbe(ITestOutputHelper output)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_tasks_each_save_one_hundred_rows_without_a_failure()
    {
        var folder = NewFolder();
        await using var host = await Boot(folder);
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
        var (read, write) = Header(folder);
        output.WriteLine($"rows={rows} failures={failures.Count} elapsed={clock.ElapsedMilliseconds}ms header[18,19]={read},{write}");
        foreach (var failure in failures) output.WriteLine(failure.ToString());

        Assert.Empty(failures);
        Assert.Equal(200, rows);
    }

    [Fact]
    public async Task A_throw_between_two_saves_in_one_transaction_persists_neither()
    {
        var folder = NewFolder();
        var probe = new TransactionProbe(ThrowBetween: true);
        await using var host = await Boot(folder, services => services.AddSingleton(probe).AddHostedService<TransactionWorker>());

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
        var folder = NewFolder();
        var probe = new TransactionProbe(ThrowBetween: false);
        await using var host = await Boot(folder, services => services.AddSingleton(probe).AddHostedService<TransactionWorker>());

        var outcome = await probe.Outcome.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        output.WriteLine(outcome.ToString());

        Assert.Null(outcome.Thrown);
        Assert.True(outcome.FirstPersisted);
        Assert.True(outcome.SecondPersisted);
    }

    [Fact]
    public async Task A_koan_transaction_writes_nothing_and_holds_no_sqlite_lock_before_commit()
    {
        var folder = NewFolder();
        await using var host = await Boot(folder);
        await new ProbeRow { Id = "seed" }.Save(Cancellation);

        using (EntityContext.Transaction("probe"))
        {
            await new ProbeRow { Id = "tracked" }.Save(Cancellation);
            Assert.Null(await ProbeRow.Get("tracked", Cancellation));

            await using var other = new SqliteConnection($"Data Source={DatabasePath(folder)};Default Timeout=1");
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
        var folder = NewFolder();
        await using var host = await Boot(folder);
        await new ProbeRow { Id = "seed" }.Save(Cancellation);
        var path = DatabasePath(folder);

        await using var first = new SqliteConnection($"Data Source={path}");
        await first.OpenAsync(Cancellation);
        await using var held = await first.BeginTransactionAsync(Cancellation);

        await using var second = new SqliteConnection($"Data Source={path};Default Timeout=1");
        await second.OpenAsync(Cancellation);
        var clock = Stopwatch.StartNew();
        var locked = Assert.Throws<SqliteException>(() => second.BeginTransaction(deferred: false));
        clock.Stop();
        output.WriteLine($"second BEGIN IMMEDIATE failed after {clock.ElapsedMilliseconds}ms: {locked.Message}");
    }

    private static Task<IntegrationHost> Boot(string folder, Action<IServiceCollection>? more = null)
        => KoanIntegrationHost.Configure()
            .WithSetting("Koan:Data:Sources:Default:Adapter", "sqlite")
            .WithSetting("Koan:Data:Sources:Default:ConnectionString", $"Data Source={DatabasePath(folder)}")
            .ConfigureServices(services =>
            {
                services.AddKoan();
                more?.Invoke(services);
            })
            .StartAsync(Cancellation);

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tangent-sqlite-posture", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string DatabasePath(string folder) => Path.Combine(folder, "probe.db");

    /// <summary>Header bytes 18 and 19: the read and write versions, 1 for rollback and 2 for write-ahead.</summary>
    private static (byte Read, byte Write) Header(string folder)
    {
        var header = new byte[20];
        using var file = new FileStream(DatabasePath(folder), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
