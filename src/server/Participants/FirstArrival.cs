using Koan.Data.Core;
using Tangent.Events;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>A signed-in person's first arrival: creates their participant with the kind they declared and the standing
/// the place's admission gives (before first run, the admission the place will be established with), and appends <see cref="EventKind.StandingChanged"/> for it. Idempotent by the person's
/// id: a person who already arrived with the same kind gets their participant back and nothing is written; another
/// kind is a conflict, since a kind is declared once.</summary>
public sealed record FirstArrival(string PersonId, string Handle, ParticipantKind Kind, DateTimeOffset? At = null)
    : ICommand<Participant>
{
    public async Task<Outcome<Participant>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Participant.Get(PersonId, ct) is { } existing)
            return existing.Kind == Kind
                ? new Outcome<Participant>.Done(existing)
                : new Outcome<Participant>.Conflict($"{PersonId} already arrived as {existing.Kind.ToString().ToLowerInvariant()}.");

        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        var participant = Participant.Arrive(PersonId, Handle, Kind, space.Admission);
        await participant.Save(ct);
        await Event.Next(sequences, EventKind.StandingChanged, At ?? DateTimeOffset.UtcNow,
            actorId: participant.Id, targetId: participant.Id).Save(ct);
        return new Outcome<Participant>.Done(participant);
    }
}
