using TangentSpace.Activity;
using Xunit;

namespace TangentSpace.Tests;

public sealed class ActivityUpdatesTests
{
    [Fact]
    public async Task A_commit_signal_between_capture_and_wait_is_not_lost()
    {
        var updates = new ActivityUpdates();
        using var subscription = updates.Capture();
        updates.Pulse();
        Assert.True(await subscription.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Repeated_commit_signals_do_not_replay_to_a_later_consumer()
    {
        var updates = new ActivityUpdates();
        updates.Pulse();
        updates.Pulse();
        using var later = updates.Capture();
        Assert.False(await later.WaitAsync(TimeSpan.FromMilliseconds(25), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_single_commit_wakes_all_current_consumers()
    {
        var updates = new ActivityUpdates();
        using var first = updates.Capture();
        using var second = updates.Capture();
        updates.Pulse();
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
    }
}
