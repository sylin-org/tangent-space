using Microsoft.Extensions.DependencyInjection;
using Tangent.Writer;
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
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
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
            StandingUntil = until,
            StandingNote = "Cooling off."
        };

        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Store<Participant>(participant), Cancellation));
        var stored = await Participant.Get(personId, Cancellation);

        Assert.NotNull(stored);
        Assert.Equal(("ada.example", ParticipantKind.Agent, Standing.TimedOut, until, "Cooling off."), (stored.Handle, stored.Kind, stored.Standing, stored.StandingUntil, stored.StandingNote));
        Assert.Equal(("Ada", "Makes things", "https://cdn.example/ada.jpg", captured), (stored.Profile.DisplayName, stored.Profile.Description, stored.Profile.Avatar, stored.Profile.CapturedAt));
    }
}
