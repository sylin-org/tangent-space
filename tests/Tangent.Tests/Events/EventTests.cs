using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Posts;
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
        await Given.ATangent(first, "the-workshop", ct: Cancellation);
        await Given.AParticipant(first, "leo", [Roles.Member], ct: Cancellation);
        await Given.AParticipant(first, "ada", [Roles.Member], ct: Cancellation);
        Assert.IsType<Outcome<Post>.Done>(await new StartTopic(topicId, "the-workshop", "Before", "First.", "leo").Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await new Reply(Tids.Mint(), topicId, "Second.", "ada").Send(Cancellation));
        Assert.Equal(2, await Event.Log.Highest(Cancellation));
        await first.StopAsync(Cancellation);

        // The second fixture's folder is deleted before the first's, which holds the data folder they share.
        await using var second = await TangentSpaceFixture.StartAsync(new Dictionary<string, string?> { ["Tangent:DataFolder"] = first.DataFolder }, ct: Cancellation);
        var after = await new Reply(Tids.Mint(), topicId, "Third.", "leo").Send(Cancellation);

        Assert.Equal(first.DataFolder, second.DataFolder);
        Assert.Equal(2L, Assert.IsType<Outcome<Post>.Done>(after).Result.Sequence);
        Assert.Equal(3, await Event.Count.Exact(Cancellation));
        Assert.Equal(3, await Event.Log.Highest(Cancellation));
        Assert.NotNull(await Event.Get(Event.Key(3), Cancellation));
    }
}
