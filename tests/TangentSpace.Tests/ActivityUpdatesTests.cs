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

    [Fact]
    public async Task A_replaced_session_wakes_its_registered_connections_with_the_identity_event()
    {
        var sessions = new LiveSessions();
        var wake = new TaskCompletionSource<LiveSessions.Identity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registered = sessions.Register("session-a", wake);
        sessions.Replaced("session-a", new LiveSessions.Identity("participant", "handle.test", "Your signed-in account changed."));
        var identity = await wake.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.Equal("participant", identity.ParticipantRef);
        Assert.Equal("handle.test", identity.BestLabel);
        Assert.Equal("Your signed-in account changed.", identity.Reason);
    }

    [Fact]
    public void Replacing_an_unknown_session_or_a_retired_connection_is_a_no_op()
    {
        var sessions = new LiveSessions();
        sessions.Replaced("unknown", new LiveSessions.Identity("participant", "handle.test", "reason"));
        var wake = new TaskCompletionSource<LiveSessions.Identity>(TaskCreationOptions.RunContinuationsAsynchronously);
        sessions.Register("session-b", wake).Dispose();
        sessions.Replaced("session-b", new LiveSessions.Identity("participant", "handle.test", "reason"));
        Assert.False(wake.Task.IsCompleted);
    }
}
