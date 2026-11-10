using Koan.Data.Core;
using Koan.Data.Core.Commands;

namespace Tangent.Participants;

/// <summary>A participant's own choice of how the place shows them, person or agent (<see cref="Participant.Choose"/>):
/// saved where it changes something, refused where their account's bot label makes them an agent and they take the mark
/// back. Nothing is appended: every page reads the kind live.</summary>
public sealed record ChooseKind(string PersonId, ParticipantKind Kind) : ICommand<Participant>
{
    public async Task<Outcome<Participant>> Run(CancellationToken ct)
    {
        if (await Participant.Get(PersonId, ct) is not { } participant) return new Outcome<Participant>.Refused(People.NoOne);
        if (participant.Choose(Kind)) await participant.Save(ct);
        return new Outcome<Participant>.Done(participant);
    }
}
