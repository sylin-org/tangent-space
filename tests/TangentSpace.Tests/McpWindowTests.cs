using TangentSpace.Conversation;
using Xunit;

namespace TangentSpace.Tests;

public sealed class McpWindowTests
{
    [Fact]
    public void Around_window_keeps_anchor_and_contiguous_neighbors()
    {
        var messages = Enumerable.Range(1, 21).Select(MessageAt).ToArray();
        var selected = McpWindowPlanner.Select(messages, 10, 5);
        Assert.Equal([9L, 10L, 11L, 12L, 13L], selected.Select(m => m.Sequence));
    }

    [Fact]
    public void Budget_never_skips_a_large_message_to_include_a_later_small_one()
    {
        var messages = Enumerable.Range(1, 5).Select(MessageAt).ToArray();
        messages[1].Content = new MessageContent(new string('x', 4096), DateTimeOffset.UnixEpoch, null);
        var selected = McpWindowPlanner.Select(messages, 0, 5, 6500);
        Assert.Single(selected);
        Assert.Equal(1, selected[0].Sequence);
    }

    [Fact]
    public void Latest_window_keeps_its_newest_message_when_budget_reduces_page()
    {
        var messages = Enumerable.Range(1, 10).Select(MessageAt).ToArray();
        foreach (var message in messages) message.Content = new MessageContent(new string('x', 4096), DateTimeOffset.UnixEpoch, null);
        var selected = McpWindowPlanner.Select(messages, 9, 10);
        Assert.Equal(10, selected[^1].Sequence);
        Assert.True(selected.Count < 10);
        Assert.Equal(Enumerable.Range((int)selected[0].Sequence, selected.Count).Select(n => (long)n), selected.Select(m => m.Sequence));
    }

    [Fact]
    public void Empty_history_has_no_invented_anchor() => Assert.Empty(McpWindowPlanner.Select([], 0, 10));

    private static Message MessageAt(int n) => new()
    {
        Id = "m" + n, RoomKey = "lounge", AuthorDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", Sequence = n,
        Content = new MessageContent("A retained message.", DateTimeOffset.UnixEpoch, null)
    };
}
