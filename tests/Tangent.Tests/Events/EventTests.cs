using Microsoft.Extensions.DependencyInjection;
using Tangent.Posts;
using Tangent.Writer;
using Xunit;

namespace Tangent.Events;

/// <summary>The log's sequence across a restart.</summary>
public sealed class EventTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_logs_high_water_mark_continues_from_the_last_event_after_the_host_stops_and_starts_again_over_the_same_folder()
    {
        await using var first = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var topicId = Tids.Mint();
        var writer = first.Services.GetRequiredService<WriteLoop>();
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(topicId, "the-workshop", "Before", "First.", "leo"), Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(Tids.Mint(), topicId, "Second.", "ada"), Cancellation));
        Assert.Equal(2, await Event.HighWaterMark.Load(Cancellation));
        await first.StopAsync(Cancellation);

        // The second fixture's folder is deleted before the first's, which holds the data folder they share.
        await using var second = await TangentSpaceFixture.StartAsync(new Dictionary<string, string?> { ["Tangent:DataFolder"] = first.DataFolder }, ct: Cancellation);
        var announced = new List<Committed>();
        var continued = second.Services.GetRequiredService<WriteLoop>();
        continued.Committed += announced.Add;
        var after = await continued.Send(new Reply(Tids.Mint(), topicId, "Third.", "leo"), Cancellation);

        Assert.Equal(first.DataFolder, second.DataFolder);
        Assert.Equal(2L, Assert.IsType<Outcome<Post>.Done>(after).Result.Sequence);
        Assert.Equal(3L, Assert.Single(announced).Sequences[Event.Log]);
        Assert.Equal(3, await Event.Count.Exact(Cancellation));
        Assert.Equal(3, await Event.HighWaterMark.Load(Cancellation));
        Assert.NotNull(await Event.Get(Event.Key(3), Cancellation));
    }
}
