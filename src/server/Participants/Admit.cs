using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>Admits an applicant, waiting or declined (<see cref="Participant.Admit"/>), and appends
/// <see cref="Events.EventKind.StandingChanged"/> with the applicant as its target, so their door tells them. Joining the
/// member role is Koan's write, made by the sender once this commits (<see cref="MemberRole"/>). The rules are
/// <see cref="Applications.Decide"/>'s.</summary>
public sealed record Admit(string ActorId, string ParticipantId, DateTimeOffset? At = null) : ICommand<Participant>
{
    public Task<Outcome<Participant>> Run(Sequences sequences, CancellationToken ct)
        => Applications.Decide(sequences, ActorId, ParticipantId, participant => participant.Admit(), At, ct);
}
