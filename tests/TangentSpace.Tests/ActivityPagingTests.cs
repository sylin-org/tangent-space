using TangentSpace.Activity;
using Xunit;

namespace TangentSpace.Tests;

public sealed class ActivityPagingTests
{
    [Fact]
    public void Event_cap_keeps_the_first_undelivered_visible_marker_for_the_next_page()
    {
        var first = ActivityPaging.Plan(0, 30, Enumerable.Range(1, 30).Select(sequence => ((long)sequence, true)).ToArray(), false, 25);
        Assert.Equal(25, first.DeliveredSequences.Count);
        Assert.Equal(25, first.After);
        Assert.True(first.HasMore);

        var second = ActivityPaging.Plan(first.After, 30, Enumerable.Range(26, 5).Select(sequence => ((long)sequence, true)).ToArray(), false, 25);
        Assert.Equal([26L, 27L, 28L, 29L, 30L], second.DeliveredSequences);
        Assert.Equal(30, second.After);
        Assert.False(second.HasMore);
    }

    [Fact]
    public void Idle_cursor_stays_byte_for_byte_stable_for_a_waiter()
    {
        var previous = new ActivityCursor("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", "consumer", 12, null,
            DateTimeOffset.Parse("2026-09-16T12:00:00Z"));
        var value = "opaque-cursor";
        Assert.Equal(value, ActivityPaging.Continue(value, previous, previous, _ => "should-not-encode"));
    }

    [Fact]
    public void Inaccessible_markers_advance_without_consuming_delivery_capacity()
    {
        var entries = new[] { (1L, false), (2L, false), (3L, true), (4L, true) };
        var plan = ActivityPaging.Plan(0, 4, entries, false, 1);
        Assert.Equal([3L], plan.DeliveredSequences);
        Assert.Equal(3, plan.After);
        Assert.True(plan.HasMore);
    }
}
