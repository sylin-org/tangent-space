using Microsoft.Extensions.DependencyInjection;
using Tangent.Posts;
using Tangent.Writer;
using Xunit;

namespace Tangent.Events;

/// <summary>A checkpoint row keyed by participant and typed scope.</summary>
public sealed class CheckpointTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_checkpoint_round_trips_through_its_participant_and_each_kind_of_scope_with_its_sequence_and_watch_flag()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var participantId = Guid.CreateVersion7().ToString("n");
        var topicId = Tids.Mint();
        var place = Scope.Place;
        var tangent = Scope.Tangent("the-workshop");
        var topic = Scope.Topic(topicId);

        Assert.IsType<Outcome<Checkpoint>.Done>(await writer.Send(new Store<Checkpoint>(Checkpoint.At(participantId, place, 7)), Cancellation));
        Assert.IsType<Outcome<Checkpoint>.Done>(await writer.Send(new Store<Checkpoint>(Checkpoint.At(participantId, tangent, 42, watch: true)), Cancellation));
        Assert.IsType<Outcome<Checkpoint>.Done>(await writer.Send(new Store<Checkpoint>(Checkpoint.At(participantId, topic, 9)), Cancellation));

        Assert.Equal(3, await Checkpoint.Count.Exact(Cancellation));
        var atPlace = await Checkpoint.Get(Checkpoint.Key(participantId, Scope.Place), Cancellation);
        var atTangent = await Checkpoint.Get(Checkpoint.Key(participantId, Scope.Tangent("the-workshop")), Cancellation);
        var atTopic = await Checkpoint.Get(Checkpoint.Key(participantId, Scope.Topic(topicId)), Cancellation);
        Assert.NotNull(atPlace);
        Assert.NotNull(atTangent);
        Assert.NotNull(atTopic);
        Assert.Equal((participantId, ScopeKind.Place, "", 7L, false), (atPlace.ParticipantId, atPlace.Scope.Kind, atPlace.Scope.Id, atPlace.Seq, atPlace.Watch));
        Assert.Equal((participantId, ScopeKind.Tangent, "the-workshop", 42L, true), (atTangent.ParticipantId, atTangent.Scope.Kind, atTangent.Scope.Id, atTangent.Seq, atTangent.Watch));
        Assert.Equal((participantId, ScopeKind.Topic, topicId, 9L, false), (atTopic.ParticipantId, atTopic.Scope.Kind, atTopic.Scope.Id, atTopic.Seq, atTopic.Watch));

        // A slug that is also the settings row's Id, or that has a TID's shape, names a scope of its own kind.
        Assert.NotEqual(Checkpoint.Key(participantId, Scope.Place), Checkpoint.Key(participantId, Scope.Tangent("tangent-space")));
        Assert.NotEqual(Checkpoint.Key(participantId, Scope.Tangent(topicId)), Checkpoint.Key(participantId, Scope.Topic(topicId)));
    }
}
