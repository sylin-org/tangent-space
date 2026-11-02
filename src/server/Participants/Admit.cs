using Koan.Data.Core.Commands;

namespace Tangent.Participants;

/// <summary>Admits an applicant, waiting or declined (<see cref="Participant.Admit"/>), and appends
/// <see cref="Events.EventKind.StandingChanged"/> with the applicant as its target, so their door tells them, and they
/// join the member role (<see cref="MemberRole"/>). The rules are <see cref="Applications.Decide"/>'s.</summary>
public sealed record Admit(string ActorId, string ParticipantId, DateTimeOffset? At = null) : ICommand<Participant>
{
    public async Task<Outcome<Participant>> Run(CancellationToken ct)
    {
        var decided = await Applications.Decide(ActorId, ParticipantId, (participant, now) => participant.Admit(now), At, ct);
        if (decided is Outcome<Participant>.Done { Result: var admitted }) await MemberRole.Join(admitted, ct);
        return decided;
    }
}
