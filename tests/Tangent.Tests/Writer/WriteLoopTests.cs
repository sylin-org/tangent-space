using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tangent.Writer;

/// <summary>The writer's proofs, each in its own Tangent Space: order and exactly-once, atomic failure, refusal,
/// idempotency by Id, high-water marks across a restart, shutdown with commands pending, and what another connection
/// sees before a commit.</summary>
public sealed class WriteLoopTests(ITestOutputHelper output)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Commands_from_several_senders_each_run_once_in_arrival_order_with_one_unbroken_sequence()
    {
        await using var tangentSpace = await Start();
        var writer = Writer(tangentSpace);
        var announced = new List<Committed>();
        writer.Committed += announced.Add;
        const int senders = 8, each = 50;

        var clock = Stopwatch.StartNew();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, senders).Select(sender => Task.Run(() =>
        {
            // Every send is queued before the next, so arrival order is this sender's order.
            var sent = Enumerable.Range(0, each)
                .Select(ordinal => writer.Send(new CreateSlip($"s{sender}-{ordinal}", $"s{sender}", ordinal, "body"), Cancellation))
                .ToList();
            return Task.WhenAll(sent);
        }, Cancellation)));
        clock.Stop();

        var slips = outcomes.SelectMany(sender => sender).Select(outcome => Assert.IsType<Outcome<Slip>.Done>(outcome).Result).ToList();
        var stored = await Slip.FirstPage(senders * each + 1, sort => sort.OrderBy(slip => slip.Sequence), Cancellation);
        output.WriteLine($"{slips.Count} commands in {clock.ElapsedMilliseconds}ms; {announced.Count} announced");

        Assert.Equal(senders * each, slips.Count);
        Assert.Equal(senders * each, await Slip.Count.Exact(Cancellation));
        Assert.Equal(Enumerable.Range(1, senders * each).Select(n => (long)n), stored.Select(slip => slip.Sequence));
        Assert.Equal(slips.Select(slip => slip.Id).Order(), stored.Select(slip => slip.Id).Order());
        foreach (var sender in stored.GroupBy(slip => slip.Sender))
        {
            Assert.Equal(sender.OrderBy(slip => slip.Ordinal).Select(slip => slip.Id), sender.Select(slip => slip.Id));
        }

        Assert.Equal(senders * each, announced.Count);
        Assert.Equal(Enumerable.Range(1, senders * each).Select(n => (long)n), announced.Select(commit => commit.Sequences[CreateSlip.Mark]));
        Assert.All(announced, commit => Assert.IsType<CreateSlip>(commit.Command));
    }

    [Fact]
    public async Task A_command_that_throws_between_two_saves_persists_neither_and_the_next_command_commits()
    {
        await using var tangentSpace = await Start();
        var writer = Writer(tangentSpace);

        var failed = await writer.Send(new WriteSlipAndStub("pair", ThrowBetween: true), Cancellation);
        var next = await writer.Send(new WriteSlipAndStub("after", ThrowBetween: false), Cancellation);

        var cause = Assert.IsType<InvalidOperationException>(Assert.IsType<Outcome<string>.Failed>(failed).Cause);
        Assert.Equal(WriteSlipAndStub.ThrownBetween, cause.Message);
        Assert.Null(await Slip.Get("pair", Cancellation));
        Assert.Null(await Stub.Get("pair", Cancellation));
        Assert.Equal("after", Assert.IsType<Outcome<string>.Done>(next).Result);
        Assert.Equal(1, (await Slip.Get("after", Cancellation))?.Sequence);
        Assert.NotNull(await Stub.Get("after", Cancellation));
    }

    [Fact]
    public async Task A_command_the_entity_refuses_commits_nothing_and_its_sender_receives_the_reason()
    {
        await using var tangentSpace = await Start();
        var writer = Writer(tangentSpace);
        var announced = 0;
        writer.Committed += _ => announced++;

        var refused = await writer.Send(new CreateSlip("blank", "s", 0, " "), Cancellation);
        var next = await writer.Send(new CreateSlip("written", "s", 1, "body"), Cancellation);

        Assert.Equal(Slip.NeedsBody, Assert.IsType<Outcome<Slip>.Refused>(refused).Reason);
        Assert.Null(await Slip.Get("blank", Cancellation));
        Assert.Equal(1, Assert.IsType<Outcome<Slip>.Done>(next).Result.Sequence);
        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task The_same_id_with_the_same_body_answers_with_the_existing_slip_and_a_different_body_is_a_conflict()
    {
        await using var tangentSpace = await Start();
        var writer = Writer(tangentSpace);

        var first = Assert.IsType<Outcome<Slip>.Done>(await writer.Send(new CreateSlip("same", "s", 0, "one"), Cancellation)).Result;
        var again = await writer.Send(new CreateSlip("same", "s", 0, "one"), Cancellation);
        var differs = await writer.Send(new CreateSlip("same", "s", 0, "two"), Cancellation);
        var after = Assert.IsType<Outcome<Slip>.Done>(await writer.Send(new CreateSlip("next", "s", 1, "three"), Cancellation)).Result;

        var existing = Assert.IsType<Outcome<Slip>.Done>(again).Result;
        Assert.Equal((first.Id, first.Sequence, first.Body), (existing.Id, existing.Sequence, existing.Body));
        Assert.Contains("'same'", Assert.IsType<Outcome<Slip>.Conflict>(differs).Reason);
        Assert.Equal("one", (await Slip.Get("same", Cancellation))?.Body);
        Assert.Equal(1, await Slip.Count.Exact(Cancellation) - 1);
        Assert.Equal(2, after.Sequence);
    }

    [Fact]
    public async Task A_sequence_continues_from_the_stored_value_after_the_host_stops_and_starts_over_the_same_folder()
    {
        await using var first = await Start();
        var writer = Writer(first);
        for (var ordinal = 0; ordinal < 3; ordinal++)
        {
            Assert.IsType<Outcome<Slip>.Done>(await writer.Send(new CreateSlip($"before-{ordinal}", "s", ordinal, "body"), Cancellation));
        }

        await first.StopAsync(Cancellation);

        // The second fixture's folder is deleted before the first's, which holds the data folder they share.
        await using var second = await Start(new Dictionary<string, string?> { ["Tangent:DataFolder"] = first.DataFolder });
        var continued = await Writer(second).Send(new CreateSlip("after", "s", 3, "body"), Cancellation);

        Assert.Equal(first.DataFolder, second.DataFolder);
        Assert.Equal(4, Assert.IsType<Outcome<Slip>.Done>(continued).Result.Sequence);
        Assert.Equal(4, await Slip.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task Stopping_with_commands_pending_ends_within_a_bound_and_each_pending_sender_receives_the_stop_as_a_failure()
    {
        var tangentSpace = await Start();
        try
        {
            var writer = Writer(tangentSpace);
            var holding = new WaitForStop();
            var held = writer.Send(holding, Cancellation);
            await holding.Started.Task.WaitAsync(Bound, Cancellation);
            var pending = Enumerable.Range(0, 3)
                .Select(ordinal => writer.Send(new CreateSlip($"pending-{ordinal}", "p", ordinal, "body"), Cancellation))
                .ToList();

            var clock = Stopwatch.StartNew();
            await tangentSpace.StopAsync(Cancellation).WaitAsync(Bound, Cancellation);
            clock.Stop();
            output.WriteLine($"stopped in {clock.ElapsedMilliseconds}ms");

            // The command in flight sees the stop as cancellation and fails; each command behind it never runs and
            // fails with the writer's own reason; a command sent after the stop fails the same way.
            Assert.InRange(clock.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(10));
            Assert.IsAssignableFrom<OperationCanceledException>(Assert.IsType<Outcome<bool>.Failed>(await held.WaitAsync(Bound, Cancellation)).Cause);
            foreach (var sender in pending)
            {
                var cause = Assert.IsType<Outcome<Slip>.Failed>(await sender.WaitAsync(Bound, Cancellation)).Cause;
                Assert.Equal(WriteLoop.Stopped, cause.Message);
            }

            var late = await writer.Send(new CreateSlip("late", "p", 9, "body"), Cancellation).WaitAsync(Bound, Cancellation);
            Assert.Equal(WriteLoop.Stopped, Assert.IsType<Outcome<Slip>.Failed>(late).Cause.Message);
            Assert.Equal(0, CountRows(tangentSpace.DatabasePath, nameof(Slip)));
        }
        finally
        {
            await tangentSpace.DisposeAsync();
        }
    }

    [Fact]
    public async Task Nothing_a_command_saved_is_visible_from_another_connection_before_its_commit()
    {
        await using var tangentSpace = await Start();
        var writer = Writer(tangentSpace);
        var hold = new HoldSlip("held");

        var sending = writer.Send(hold, Cancellation);
        await hold.Saved.Task.WaitAsync(Bound, Cancellation);
        var seenThroughKoan = await Slip.Get("held", Cancellation) is not null;
        var rowsFromOutside = CountRows(tangentSpace.DatabasePath, nameof(Slip));
        hold.Release.SetResult();
        var outcome = await sending.WaitAsync(Bound, Cancellation);

        Assert.False(hold.VisibleToItselfBeforeCommit);
        Assert.False(seenThroughKoan);
        Assert.Equal(0, rowsFromOutside);
        Assert.Equal(1, Assert.IsType<Outcome<Slip>.Done>(outcome).Result.Sequence);
        Assert.NotNull(await Slip.Get("held", Cancellation));
        Assert.Equal(1, CountRows(tangentSpace.DatabasePath, nameof(Slip)));
    }

    /// <summary>A Tangent Space with the slips' high-water mark registered.</summary>
    private static Task<TangentSpaceFixture> Start(IReadOnlyDictionary<string, string?>? settings = null) =>
        TangentSpaceFixture.StartAsync(settings, services => services.AddSingleton(CreateSlip.HighWaterMark), Cancellation);

    private static WriteLoop Writer(TangentSpaceFixture tangentSpace) => tangentSpace.Services.GetRequiredService<WriteLoop>();

    /// <summary>Rows in the entity's table, counted on a connection of the test's own, outside the driver's pool; zero
    /// when the table does not exist yet.</summary>
    private static long CountRows(string databasePath, string entity)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var tables = connection.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE $suffix";
        tables.Parameters.AddWithValue("$suffix", "%" + entity);
        var table = tables.ExecuteScalar() as string;
        if (table is null) return 0;
        using var count = connection.CreateCommand();
        count.CommandText = $"SELECT count(*) FROM \"{table}\"";
        return (long)count.ExecuteScalar()!;
    }
}
