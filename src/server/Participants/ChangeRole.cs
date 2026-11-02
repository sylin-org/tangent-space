using Koan.Core.Hosting.App;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Events;

namespace Tangent.Participants;

/// <summary>Gives a participant one of the roles given here (<see cref="Roles.Given"/>: every role but everyone and
/// member), or takes it from them where <see cref="Give"/> is false. Judged by <see cref="People.Judge"/> for
/// <see cref="Bit.ManageRoles"/>; the role is one given here, or the command is refused with
/// <see cref="People.NotARole"/>; the actor manages it (<see cref="People.Manages"/>), or is refused with
/// <see cref="People.NotManaged"/>; the actor holds every permission it grants, or is refused with
/// <see cref="People.NotHeld"/>. The change appends one <see cref="EventKind.RoleChanged"/> with the actor, the
/// participant as its target, the role and whether it was given, so their door tells them; a role already as asked writes
/// nothing.</summary>
public sealed record ChangeRole(string ActorId, string ParticipantId, string Role, bool Give, DateTimeOffset? At = null) : ICommand<Participant>
{
    public async Task<Outcome<Participant>> Run(CancellationToken ct)
    {
        var now = At ?? DateTimeOffset.UtcNow;
        var judged = await People.Judge(ActorId, ParticipantId, Bit.ManageRoles, now, ct);
        if (judged.Refusal is { } refusal) return new Outcome<Participant>.Refused(refusal);
        if (!Roles.Given(Role)) return new Outcome<Participant>.Refused(People.NotARole);
        var roles = AppHost.GetRequiredService<RoleCollection>(nameof(ChangeRole));
        if (await roles.Get(Role, ct) is not { } role) return new Outcome<Participant>.Refused(People.NotARole);
        if (!People.Manages(judged.Actor!, ActorId, judged.Space!, role)) return new Outcome<Participant>.Refused(People.NotManaged);
        if (People.Holding(judged.Actor!, role) is { } notHeld) return new Outcome<Participant>.Refused(notHeld);

        var target = judged.Target!;
        if (judged.Bag!.InRole(Role) == Give) return new Outcome<Participant>.Done(target);
        if (Give) await roles.Add(Role, target.Id, ct);
        else await roles.Remove(Role, target.Id, ct);
        await Event.Append(EventKind.RoleChanged, now, ct, actorId: ActorId, targetId: target.Id, role: Role, given: Give);
        return new Outcome<Participant>.Done(target);
    }
}
