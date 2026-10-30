using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>Times a participant out until <see cref="Until"/> (<see cref="Participant.TimeOut"/>): until then they read
/// and report, and post, reply and start nothing. The rules are <see cref="People.ChangeStanding"/>'s, and its <see cref="Events.EventKind.StandingChanged"/>
/// tells the participant's door.</summary>
public sealed record TimeOut(string ActorId, string ParticipantId, DateTimeOffset Until, DateTimeOffset? At = null) : ICommand<Participant>
{
    public Task<Outcome<Participant>> Run(Sequences sequences, CancellationToken ct)
        => People.ChangeStanding(sequences, ActorId, ParticipantId, (participant, now) => participant.TimeOut(Until, now), At, ct);
}
