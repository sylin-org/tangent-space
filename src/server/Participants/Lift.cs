using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>Lifts a participant's timeout or ban (<see cref="Participant.Lift"/>): they stand admitted again, and their
/// door says they are back. The rules are <see cref="People.ChangeStanding"/>'s; the member role, which standing never
/// took away, is joined by the sender once this commits where it is missing (<see cref="MemberRole"/>).</summary>
public sealed record Lift(string ActorId, string ParticipantId, DateTimeOffset? At = null) : ICommand<Participant>
{
    public Task<Outcome<Participant>> Run(Sequences sequences, CancellationToken ct)
        => People.ChangeStanding(sequences, ActorId, ParticipantId, (participant, now) => participant.Lift(now), At, ct);
}
