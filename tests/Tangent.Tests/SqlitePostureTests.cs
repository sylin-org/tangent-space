using System.Collections.Concurrent;
using System.Diagnostics;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tangent;

/// <summary>A row the posture tests write. It exists only here.</summary>
public sealed class ProbeRow : Entity<ProbeRow>
{
    public string Lane { get; set; } = "";

    public int Number { get; set; }
}

/// <summary>Koan's SQLite posture in the Tangent Space's own database: write-ahead logging and the busy timeout. Each fact
/// starts its own Tangent Space.</summary>
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
