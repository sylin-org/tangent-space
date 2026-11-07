using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Events;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>A person's first sign-in: creates their participant, bound to the account <see cref="Did"/>, a person until their profile says otherwise, with the
/// standing the place's admission gives (before first run, the admission the place will be established with), or admitted
/// where an <see cref="Invitation"/> names the account, which it uses up whatever admission says now, and appends
/// <see cref="EventKind.StandingChanged"/> for it, carrying that standing; an applicant's application waits at that
/// event's sequence (<see cref="Participant.WaitingSeq"/>), and that is all it writes, however many may decide it; anyone
/// admitted joins the member role (<see cref="MemberRole"/>). What is new to them begins at that event
/// (<see cref="Participant.ArrivedSeq"/>), so what the place held before is not news to them. Idempotent by the person's id: a person who already arrived
/// gets their participant back and nothing is written. The handle is taken from any other participant who still keeps it
/// (<see cref="Handles.Release"/>).</summary>
public sealed record FirstArrival(string PersonId, string Did, string Handle, DateTimeOffset? At = null) : ICommand<Participant>
{
    public async Task<Outcome<Participant>> Run(CancellationToken ct)
    {
        if (await Participant.Get(PersonId, ct) is { } existing) return new Outcome<Participant>.Done(existing);

        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        var at = At ?? DateTimeOffset.UtcNow;
        var invitation = Participant.IsDid(Did) ? await Invitation.Get(Did, ct) : null;
        var participant = Participant.Arrive(PersonId, Did, Handle, invitation is null ? space.Admission : Admission.Join, at);
        var arrival = await Event.Append(EventKind.StandingChanged, at, ct, actorId: participant.Id, targetId: participant.Id,
            standing: participant.Standing);
        participant.Arrived(arrival.Seq);
        await participant.Save(ct);
        if (invitation is not null) await invitation.Remove(ct);
        await Handles.Release(participant.Handle, participant.Id, ct);
        await MemberRole.Join(participant, ct);
        return new Outcome<Participant>.Done(participant);
    }
}
