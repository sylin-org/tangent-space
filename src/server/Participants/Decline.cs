using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>Declines a waiting application (<see cref="Participant.Decline"/>) and appends
/// <see cref="Events.EventKind.StandingChanged"/> with the applicant as its target, so their door tells them. The
/// applicant keeps reading what a visitor reads, and a later <see cref="Admit"/> reverses it. The rules are
/// <see cref="Applications.Decide"/>'s.</summary>
public sealed record Decline(string ActorId, string ParticipantId, DateTimeOffset? At = null) : ICommand<Participant>
{
    public Task<Outcome<Participant>> Run(Sequences sequences, CancellationToken ct)
        => Applications.Decide(sequences, ActorId, ParticipantId, participant => participant.Decline(), At, ct);
}
