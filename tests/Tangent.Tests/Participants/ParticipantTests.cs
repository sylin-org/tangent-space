using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tangent.Participants;

/// <summary>A participant row keyed by the Koan person id.</summary>
public sealed class ParticipantTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_participant_round_trips_through_the_person_id_with_its_handle_profile_kind_and_standing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var personId = Guid.CreateVersion7().ToString("n");
        var until = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var captured = new DateTimeOffset(2026, 10, 29, 8, 30, 0, TimeSpan.Zero);
        var participant = new Participant
        {
            Id = personId,
            Handle = "ada.example",
            Profile = new Profile { DisplayName = "Ada", Description = "Makes things", Avatar = "https://cdn.example/ada.jpg", CapturedAt = captured },
            Kind = ParticipantKind.Agent,
            Standing = Standing.TimedOut,
            StandingUntil = until
        };

        Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(participant).Send(Cancellation));
        var stored = await Participant.Get(personId, Cancellation);

        Assert.NotNull(stored);
        Assert.Equal(("ada.example", ParticipantKind.Agent, Standing.TimedOut, until), (stored.Handle, stored.Kind, stored.Standing, stored.StandingUntil));
        Assert.Equal(("Ada", "Makes things", "https://cdn.example/ada.jpg", captured), (stored.Profile.DisplayName, stored.Profile.Description, stored.Profile.Avatar, stored.Profile.CapturedAt));
    }

    /// <summary>The command and the page read the standing in force, Koan's rule the stored one: a timeout that has ended
    /// stands admitted while it stays stored as timed out, so every change offered from admitted must be one from timed
    /// out, or leave the stored standing as it is.</summary>
    [Fact]
    public void Every_change_from_admitted_is_one_the_stored_timeout_takes()
        => Assert.All(Participant.From(Standing.Admitted), transition => Assert.True(transition.To == Standing.TimedOut
            || Participant.From(Standing.TimedOut).Any(stored => stored.To == transition.To), $"{transition.To} is not taken from a timeout."));

    [Fact]
    public async Task A_save_that_changes_a_standing_outside_the_table_is_refused_whoever_saves_and_a_waiting_application_compiles_where_it_waits()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var personId = Guid.CreateVersion7().ToString("D");
        var arrived = Participant.Arrive(personId, "did:plc:standing", "standing.example", Space.Admission.Apply, DateTimeOffset.UtcNow);
        arrived.Arrived(7);
        Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(arrived).Send(Cancellation));
        var waiting = (await Participant.Get(personId, Cancellation))!;
        Assert.Equal(7, waiting.WaitingSeq);

        waiting.Standing = Standing.Banned;
        var refused = await Assert.ThrowsAsync<Refusal>(() => waiting.Save(Cancellation));
        Assert.Equal((ReasonKind.Conflict, "transition"), (refused.Reason.Kind, refused.Reason.Word));

        // Admitted, the row keeps neither when they applied nor where they waited.
        waiting.Standing = Standing.Admitted;
        Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(waiting).Send(Cancellation));
        var admitted = (await Participant.Get(personId, Cancellation))!;
        Assert.Equal((Standing.Admitted, (DateTimeOffset?)null, (long?)null), (admitted.Standing, admitted.AppliedAt, admitted.WaitingSeq));
    }
}
