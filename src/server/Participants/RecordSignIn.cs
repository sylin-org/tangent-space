using Koan.Data.Core;
using Koan.Data.Core.Commands;

namespace Tangent.Participants;

/// <summary>At a sign-in, the account the connector verified for the participant: its DID, which binds them to it, and
/// its handle, or empty when it verified none, so the handle the place shows is always the connector's. Nothing is
/// written when neither changed; a person without a participant is refused, since their first arrival takes both itself. The handle is taken from any
/// other participant who still keeps it (<see cref="Handles.Release"/>), and a participant who stands admitted now, a
/// timeout that has ended among them, joins the member role where they lack it (<see cref="MemberRole"/>).</summary>
public sealed record RecordSignIn(string PersonId, string Did, string Handle) : ICommand<Participant>
{
    public async Task<Outcome<Participant>> Run(CancellationToken ct)
    {
        if (await Participant.Get(PersonId, ct) is not { } participant)
            return new Outcome<Participant>.Refused(Reason.Invalid($"{PersonId} has not arrived."));
        if (participant.Verified(Did, Handle)) await participant.Save(ct);
        await Handles.Release(participant.Handle, participant.Id, ct);
        await MemberRole.Join(participant, ct);
        return new Outcome<Participant>.Done(participant);
    }
}
