using Koan.Data.Core;
using Tangent.Events;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>A person's first sign-in: creates their participant, a person until their profile says otherwise, with the
/// standing the place's admission gives (before first run, the admission the place will be established with), and appends
/// <see cref="EventKind.StandingChanged"/> for it, carrying that standing; an applicant's application waits at that
/// event's sequence (<see cref="Participant.WaitingSeq"/>), and that is all it writes, however many may decide it.
/// Idempotent by the person's id: a person who already arrived gets their participant back and nothing is written. The handle is taken from any other participant who still keeps it (<see cref="Handles.Release"/>).</summary>
public sealed record FirstArrival(string PersonId, string Handle, DateTimeOffset? At = null) : ICommand<Participant>
{
    public async Task<Outcome<Participant>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Participant.Get(PersonId, ct) is { } existing) return new Outcome<Participant>.Done(existing);

        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        var at = At ?? DateTimeOffset.UtcNow;
        var participant = Participant.Arrive(PersonId, Handle, space.Admission, at);
        var arrival = Event.Next(sequences, EventKind.StandingChanged, at, actorId: participant.Id, targetId: participant.Id,
            standing: participant.Standing);
        participant.Arrived(arrival.Seq);
        await participant.Save(ct);
        await Handles.Release(participant.Handle, participant.Id, ct);
        await arrival.Save(ct);
        return new Outcome<Participant>.Done(participant);
    }
}
