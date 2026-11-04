using Koan.Data.Core.Commands;
using Tangent.Posts;
using Xunit;

namespace Tangent.Events;

/// <summary>A checkpoint row keyed by participant and typed scope.</summary>
public sealed class CheckpointTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_checkpoint_round_trips_through_its_participant_and_each_kind_of_scope_with_its_sequence()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var participantId = Guid.CreateVersion7().ToString("n");
        var topicId = Tids.Mint();

        Assert.IsType<Outcome<Checkpoint>.Done>(await new Store<Checkpoint>(Checkpoint.At(participantId, Scope.Place, 7)).Send(Cancellation));
        Assert.IsType<Outcome<Checkpoint>.Done>(await new Store<Checkpoint>(Checkpoint.At(participantId, Scope.Topic(topicId), 9)).Send(Cancellation));

        Assert.Equal(2, await Checkpoint.Count.Exact(Cancellation));
        var atPlace = await Checkpoint.Get(Checkpoint.Key(participantId, Scope.Place), Cancellation);
        var atTopic = await Checkpoint.Get(Checkpoint.Key(participantId, Scope.Topic(topicId)), Cancellation);
        Assert.NotNull(atPlace);
        Assert.NotNull(atTopic);
        Assert.Equal((participantId, ScopeKind.Place, "", 7L), (atPlace.ParticipantId, atPlace.Scope.Kind, atPlace.Scope.Id, atPlace.Seq));
        Assert.Equal((participantId, ScopeKind.Topic, topicId, 9L), (atTopic.ParticipantId, atTopic.Scope.Kind, atTopic.Scope.Id, atTopic.Seq));

        // An Id that reads as the place's own word still names a scope of its own kind.
        Assert.NotEqual(Checkpoint.Key(participantId, Scope.Place), Checkpoint.Key(participantId, Scope.Topic("place")));
    }
}
