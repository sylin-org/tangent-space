using System.Diagnostics;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tangent.Commands;

/// <summary>What Tangent's writes rest on, each in its own Tangent Space: one unbroken sequence in each sender's order,
/// atomic failure, refusal, idempotency by Id, a sequence across a restart, and what a command and another connection
/// see before its commit. Koan's own specs prove the turn, the bounds and what a failed commit reports.</summary>
public sealed class CommandTests(ITestOutputHelper output)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Commands_from_several_senders_each_run_once_in_each_senders_order_with_one_unbroken_sequence()
    {
        await using var tangentSpace = await Start();
        const int senders = 8, each = 50;

        var clock = Stopwatch.StartNew();
        var outcomes = await Task.WhenAll(Enumerable.Range(0, senders).Select(sender => Task.Run(() =>
        {
            // Every send reaches the store's turn before the next, so the turn holds this sender's order.
            var sent = Enumerable.Range(0, each)
                .Select(ordinal => new CreateSlip($"s{sender}-{ordinal}", $"s{sender}", ordinal, "body").Send(Cancellation))
                .ToList();
            return Task.WhenAll(sent);
        }, Cancellation)));
        clock.Stop();

        var slips = outcomes.SelectMany(sender => sender).Select(outcome => Assert.IsType<Outcome<Slip>.Done>(outcome).Result).ToList();
        var stored = await Slip.FirstPage(senders * each + 1, sort => sort.OrderBy(slip => slip.Sequence), Cancellation);
        output.WriteLine($"{slips.Count} commands in {clock.ElapsedMilliseconds}ms");

        Assert.Equal(senders * each, slips.Count);
        Assert.Equal(senders * each, await Slip.Count.Exact(Cancellation));
        Assert.Equal(Enumerable.Range(1, senders * each).Select(n => (long)n), stored.Select(slip => slip.Sequence));
        Assert.Equal(slips.Select(slip => slip.Id).Order(), stored.Select(slip => slip.Id).Order());
        foreach (var sender in stored.GroupBy(slip => slip.Sender))
        {
            Assert.Equal(sender.OrderBy(slip => slip.Ordinal).Select(slip => slip.Id), sender.Select(slip => slip.Id));
        }

    }

    [Fact]
    public async Task A_command_that_throws_between_two_saves_persists_neither_and_the_next_command_commits()
    {
        await using var tangentSpace = await Start();

        var failed = await new WriteSlipAndStub("pair", ThrowBetween: true).Send(Cancellation);
        var next = await new WriteSlipAndStub("after", ThrowBetween: false).Send(Cancellation);

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

        var refused = await new CreateSlip("blank", "s", 0, " ").Send(Cancellation);
        var next = await new CreateSlip("written", "s", 1, "body").Send(Cancellation);

        Assert.Equal(Slip.NeedsBody, Assert.IsType<Outcome<Slip>.Refused>(refused).Reason);
        Assert.Null(await Slip.Get("blank", Cancellation));
        Assert.Equal(1, Assert.IsType<Outcome<Slip>.Done>(next).Result.Sequence);
        Assert.Equal(1, await Slip.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task The_same_id_with_the_same_body_answers_with_the_existing_slip_and_a_different_body_is_a_conflict()
    {
        await using var tangentSpace = await Start();

        var first = Assert.IsType<Outcome<Slip>.Done>(await new CreateSlip("same", "s", 0, "one").Send(Cancellation)).Result;
        var again = await new CreateSlip("same", "s", 0, "one").Send(Cancellation);
        var differs = await new CreateSlip("same", "s", 0, "two").Send(Cancellation);
        var after = Assert.IsType<Outcome<Slip>.Done>(await new CreateSlip("next", "s", 1, "three").Send(Cancellation)).Result;

        // The replay answers with the existing slip and, having written nothing, takes no number: the next slip has the second.
        var existing = Assert.IsType<Outcome<Slip>.Done>(again).Result;
        Assert.Equal((first.Id, first.Sequence, first.Body), (existing.Id, existing.Sequence, existing.Body));
        var conflict = Assert.IsType<Outcome<Slip>.Refused>(differs).Reason;
        Assert.Equal(ReasonKind.Conflict, conflict.Kind);
        Assert.Contains("'same'", conflict.Text);
        Assert.Equal("one", (await Slip.Get("same", Cancellation))?.Body);
        Assert.Equal(2, await Slip.Count.Exact(Cancellation));
        Assert.Equal(2, after.Sequence);
    }

    [Fact]
    public async Task A_sequence_continues_from_the_stored_value_after_the_host_stops_and_starts_over_the_same_folder()
    {
        await using var first = await Start();
        for (var ordinal = 0; ordinal < 3; ordinal++)
        {
            Assert.IsType<Outcome<Slip>.Done>(await new CreateSlip($"before-{ordinal}", "s", ordinal, "body").Send(Cancellation));
        }

        await first.StopAsync(Cancellation);

        // The second fixture's folder is deleted before the first's, which holds the data folder they share.
        await using var second = await TangentSpaceFixture.StartAsync(new Dictionary<string, string?> { ["Tangent:DataFolder"] = first.DataFolder }, ct: Cancellation);
        var continued = await new CreateSlip("after", "s", 3, "body").Send(Cancellation);

        Assert.Equal(first.DataFolder, second.DataFolder);
        Assert.Equal(4, Assert.IsType<Outcome<Slip>.Done>(continued).Result.Sequence);
        Assert.Equal(4, await Slip.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task A_command_sees_its_own_save_and_no_other_connection_does_before_its_commit()
    {
        await using var tangentSpace = await Start();
        Assert.IsType<Outcome<Slip>.Done>(await new CreateSlip("first", "s", 0, "body").Send(Cancellation));
        var hold = new HoldSlip("held");

        var sending = hold.Send(Cancellation);
        await hold.Saved.Task.WaitAsync(Bound, Cancellation);
        var seenThroughKoan = await Slip.Get("held", Cancellation) is not null;
        var rowsFromOutside = CountRows(tangentSpace.DatabasePath, nameof(Slip));
        hold.Release.SetResult();
        var outcome = await sending.WaitAsync(Bound, Cancellation);

        Assert.True(hold.VisibleToItselfBeforeCommit);
        Assert.False(seenThroughKoan);
        Assert.Equal(1, rowsFromOutside);
        Assert.Equal(2, Assert.IsType<Outcome<Slip>.Done>(outcome).Result.Sequence);
        Assert.NotNull(await Slip.Get("held", Cancellation));
        Assert.Equal(2, CountRows(tangentSpace.DatabasePath, nameof(Slip)));
    }

    private static Task<TangentSpaceFixture> Start() => TangentSpaceFixture.StartAsync(ct: Cancellation);

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
