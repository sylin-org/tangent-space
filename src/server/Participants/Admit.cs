using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Events;

namespace Tangent.Participants;

/// <summary>Admits an applicant, waiting or declined (<see cref="Participant.Admit"/>), and appends
/// <see cref="EventKind.StandingChanged"/> with the applicant as its target, so their door tells them, and they
/// join the member role (<see cref="MemberRole"/>). Their place checkpoint begins just before that event, so their door
/// tells the admission and nothing posted while they waited reads as news. The rules are
/// <see cref="Applications.Decide"/>'s.</summary>
public sealed record Admit(string ActorId, string ParticipantId, DateTimeOffset? At = null) : ICommand<Participant>
{
    public async Task<Outcome<Participant>> Run(CancellationToken ct)
    {
        var waited = await Event.Log.Highest(ct);
        var changed = false;
        var decided = await Applications.Decide(ActorId, ParticipantId, (participant, now) => changed = participant.Admit(now), At, ct);
        if (decided is not Outcome<Participant>.Done { Result: var admitted }) return decided;
        await MemberRole.Join(admitted, ct);
        if (changed) await Checkpoint.At(admitted.Id, Scope.Place, waited).Save(ct);
        return decided;
    }
}
