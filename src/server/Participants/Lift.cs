using Koan.Data.Core.Commands;

namespace Tangent.Participants;

/// <summary>Lifts a participant's timeout or ban (<see cref="Participant.Lift"/>): they stand admitted again, and their
/// door says they are back. The rules are <see cref="People.ChangeStanding"/>'s; the member role, which standing never
/// took away, is joined where it is missing (<see cref="MemberRole"/>).</summary>
public sealed record Lift(string ActorId, string ParticipantId, DateTimeOffset? At = null) : ICommand<Participant>
{
    public async Task<Outcome<Participant>> Run(CancellationToken ct)
    {
        var lifted = await People.ChangeStanding(ActorId, ParticipantId, (participant, now) => participant.Lift(now), At, ct);
        if (lifted is Outcome<Participant>.Done { Result: var participant }) await MemberRole.Join(participant, ct);
        return lifted;
    }
}
