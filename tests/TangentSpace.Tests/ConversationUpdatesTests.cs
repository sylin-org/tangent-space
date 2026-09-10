using TangentSpace.Conversation;
using Xunit;

namespace TangentSpace.Tests;

public sealed class ConversationUpdatesTests
{
    [Fact]
    public async Task A_pulse_between_capture_and_wait_is_not_lost()
    {
        var updates = new ConversationUpdates();
        using var captured = updates.Capture("workshop");
        updates.Pulse("workshop");
        Assert.True(await captured.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_pulse_wakes_all_waiters_only_in_its_room()
    {
        var updates = new ConversationUpdates();
        using var first = updates.Capture("workshop");
        using var second = updates.Capture("workshop");
        using var other = updates.Capture("lounge");
        var firstWait = first.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var secondWait = second.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        updates.Pulse("workshop");
        Assert.True(await firstWait);
        Assert.True(await secondWait);
        Assert.False(await other.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task New_capture_does_not_replay_an_old_pulse_or_lose_its_own_generation()
    {
        var updates = new ConversationUpdates();
        var old = updates.Capture("workshop");
        updates.Pulse("workshop");
        using var current = updates.Capture("workshop");
        old.Dispose();
        Assert.False(await current.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
        updates.Pulse("workshop");
        Assert.True(await current.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Request_cancellation_does_not_cancel_another_subscriber()
    {
        var updates = new ConversationUpdates();
        using var first = updates.Capture("workshop");
        using var second = updates.Capture("workshop");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var canceled = first.WaitAsync(Timeout.InfiniteTimeSpan, cancellation.Token);
        var surviving = second.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
        Assert.False(surviving.IsCompleted);
        updates.Pulse("workshop");
        Assert.True(await surviving);
    }

    [Fact]
    public async Task Timeout_does_not_consume_a_later_notification()
    {
        var updates = new ConversationUpdates();
        using var captured = updates.Capture("workshop");
        Assert.False(await captured.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
        updates.Pulse("workshop");
        Assert.True(await captured.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Disposing_one_subscriber_keeps_the_other_subscribed()
    {
        var updates = new ConversationUpdates();
        var first = updates.Capture("workshop");
        using var second = updates.Capture("workshop");
        first.Dispose();
        first.Dispose();
        updates.Pulse("workshop");
        Assert.True(await second.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Pulsing_without_subscribers_does_not_leave_a_stale_notification()
    {
        var updates = new ConversationUpdates();
        using (updates.Capture("workshop")) { }
        updates.Pulse("workshop");
        using var captured = updates.Capture("workshop");
        Assert.False(await captured.WaitAsync(TimeSpan.Zero, TestContext.Current.CancellationToken));
    }
}
