using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Events;

namespace Tangent.Participants;

/// <summary>Gives the participant <see cref="ParticipantId"/> the standing <see cref="To"/>, a timeout until
/// <see cref="Until"/>: the one change of standing after arrival. The actor is judged by <see cref="People.Judge"/> with
/// <see cref="Bit.ManageStanding"/>, and Koan refuses a save outside <see cref="Participant.Standings"/>. Giving the standing
/// they stand at now changes nothing and writes nothing; a change appends one <see cref="EventKind.StandingChanged"/> with the
/// actor, the participant as its target, the standing given and the one in force before, so their door tells them, and one
/// who stands admitted joins the member role where they lack it (<see cref="MemberRole"/>).</summary>
public sealed record SetStanding(string ActorId, string ParticipantId, Standing To, DateTimeOffset? Until = null, DateTimeOffset? At = null)
    : ICommand<Participant>
{
    public async Task<Outcome<Participant>> Run(CancellationToken ct)
    {
        var now = At ?? DateTimeOffset.UtcNow;
        var judged = await People.Judge(ActorId, ParticipantId, Bit.ManageStanding, now, ct);
        if (judged.Refusal is { } refusal) return new Outcome<Participant>.Refused(refusal);
        var target = judged.Target!;
        var before = target.StandingAt(now);
        if (before == To) return new Outcome<Participant>.Done(target);
        if (To == Standing.TimedOut && !(Until > now)) return new Outcome<Participant>.Refused(Participant.TimeoutEnds);

        (target.Standing, target.StandingUntil) = (To, Until);
        await target.Save(ct);
        await Event.Append(EventKind.StandingChanged, now, ct, actorId: ActorId, targetId: target.Id, standing: To, before: before);
        await MemberRole.Join(target, ct);
        return new Outcome<Participant>.Done(target);
    }
}
