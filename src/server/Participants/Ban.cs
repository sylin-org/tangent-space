using Koan.Data.Core.Commands;

namespace Tangent.Participants;

/// <summary>Bans a participant (<see cref="Participant.Ban"/>): the place is absent to them until someone lifts it. The
/// rules are <see cref="People.ChangeStanding"/>'s.</summary>
public sealed record Ban(string ActorId, string ParticipantId, DateTimeOffset? At = null) : ICommand<Participant>
{
    public Task<Outcome<Participant>> Run(CancellationToken ct)
        => People.ChangeStanding(ActorId, ParticipantId, (participant, now) => participant.Ban(now), At, ct);
}
