using Microsoft.Extensions.DependencyInjection;
using Tangent.Writer;
using Xunit;

namespace Tangent.Events;

/// <summary>A checkpoint row keyed by participant and scope.</summary>
public sealed class CheckpointTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_checkpoint_round_trips_through_its_participant_and_scope_with_its_sequence_and_watch_flag()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var participantId = Guid.CreateVersion7().ToString("n");

        Assert.IsType<Outcome<Checkpoint>.Done>(await writer.Send(new Store<Checkpoint>(Checkpoint.At(participantId, "the-workshop", 42, watch: true)), Cancellation));
        var stored = await Checkpoint.Get(Checkpoint.Key(participantId, "the-workshop"), Cancellation);

        Assert.NotNull(stored);
        Assert.Equal((participantId, "the-workshop", 42L, true), (stored.ParticipantId, stored.ScopeId, stored.Seq, stored.Watch));
    }
}
