using Koan.Data.Core;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>At a sign-in, the handle the connector verified for the participant, or empty when it verified none:
/// the handle the place shows is always the connector's. Nothing is written when it has not changed; a person
/// without a participant is refused, since their first arrival takes the handle itself.</summary>
public sealed record RecordHandle(string PersonId, string Handle) : ICommand<Participant>
{
    public async Task<Outcome<Participant>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Participant.Get(PersonId, ct) is not { } participant)
            return new Outcome<Participant>.Refused($"{PersonId} has not arrived.");
        if (participant.Verified(Handle)) await participant.Save(ct);
        return new Outcome<Participant>.Done(participant);
    }
}
