using Koan.Data.Abstractions.Failures;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Transactions;
using Tangent.Commands;
using Tangent.Events;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>One commit or nothing: a write command that fails anywhere — thrown after its saves, or refused by the store
/// at commit on its last row — leaves no row, no snapshot and no event behind; and a snapshot in the audit partition is
/// part of its command's one native transaction.</summary>
public sealed class AtomicityTests
{
    [Fact]
    public async Task A_command_that_fails_after_its_first_save_leaves_no_row_snapshot_or_event()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var before = (await Counted(), await Snapshots());
        var topicId = Tids.Mint();

        var start = await new ThenThrow<Post>(new StartTopic(topicId, Workshop, "Never", "Not kept.", Ada)).Send(Cancellation);
        var edit = await new ThenThrow<Post>(new EditPost(root.Id, 1, "Not kept.", null, Ada)).Send(Cancellation);
        var remove = await new ThenThrow<Post>(new RemovePost(root.Id, Ada)).Send(Cancellation);

        foreach (var failed in new[] { start, edit, remove })
            Assert.Equal(ThenThrow<Post>.Thrown, Assert.IsType<Outcome<Post>.Failed>(failed).Cause.Message);
        Assert.Null(await Post.Get(topicId, Cancellation));
        var stored = await Post.Get(root.Id, Cancellation);
        Assert.Equal(("How do you hang yours?", 1L, false), (stored?.Body, stored?.Version, stored?.Removed));
        Assert.Null(await AuditPartition.At<Post>(root.Id, 1, Cancellation));
        Assert.Equal(before.Item2, await Snapshots());
        Assert.Equal(before.Item1, await Counted());
        Assert.Empty(await Event.Query(e => e.Kind == EventKind.PostEdited || e.Kind == EventKind.PostRemoved, Cancellation));
    }

    [Fact]
    public async Task A_snapshot_in_the_audit_partition_commits_in_one_native_transaction_with_its_commands_rows_or_not_at_all()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);

        var kept = await new SlipWithSnapshot("kept").Send(Cancellation);
        var lost = await new ThenThrow<Slip>(new SlipWithSnapshot("lost")).Send(Cancellation);

        Assert.Equal("after", Assert.IsType<Outcome<Slip>.Done>(kept).Result.Body);
        Assert.Equal(("before", "after"), ((await AuditPartition.At<Slip>("kept", 1, Cancellation))?.Body, (await Slip.Get("kept", Cancellation))?.Body));
        Assert.Equal(DataCommitOutcome.NotCommitted, Assert.IsType<Outcome<Slip>.Failed>(lost).Landed);
        Assert.Null(await Slip.Get("lost", Cancellation));
        Assert.Null(await AuditPartition.At<Slip>("lost", 1, Cancellation));
        Assert.Single(await Data<Slip, string>.All(AuditPartition.Name, Cancellation));
    }

    /// <summary>Runs a command, then throws, so every save it made rolls back.</summary>
    private sealed record ThenThrow<TResult>(ICommand<TResult> Inner) : ICommand<TResult>
    {
        public const string Thrown = "thrown after the command's saves";

        public async Task<Outcome<TResult>> Run(CancellationToken ct)
        {
            Assert.IsType<Outcome<TResult>.Done>(await Inner.Run(ct));
            throw new InvalidOperationException(Thrown);
        }
    }

    /// <summary>Saves a slip, its snapshot at version one in the audit partition, the slip changed, and a log row, in that
    /// order.</summary>
    private sealed record SlipWithSnapshot(string Id) : ICommand<Slip>
    {
        public async Task<Outcome<Slip>> Run(CancellationToken ct)
        {
            var slip = new Slip { Id = Id, Sender = "audit", Body = "before" };
            await AuditPartition.Snapshot(slip, 1, ct);
            slip.Body = "after";
            await slip.Save(ct);
            await Event.Append("slip.changed", DateTimeOffset.UtcNow, ct);
            return new Outcome<Slip>.Done(slip);
        }
    }
}
