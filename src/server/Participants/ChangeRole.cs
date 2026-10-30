using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>Gives a participant one of the roles given here (<see cref="People.Given"/>: moderator or administrator), or
/// takes it from them where <see cref="Give"/> is false. Judged by <see cref="People.Judge"/> for
/// <see cref="Bit.ManageRoles"/>; the role is one given here, or the command is refused with
/// <see cref="People.NotARole"/>; the actor holds every permission it grants, or is refused with
/// <see cref="People.NotHeld"/>. Koan's role write is not part of a transaction, so <see cref="Prepare"/> judges the change
/// and makes it before the transaction opens, and <see cref="Run"/>, in the same turn of the loop, keeps that judgement and
/// appends one <see cref="EventKind.RoleChanged"/> with the actor, the participant as its target, the role and whether it
/// was given, so their door tells them; a role already as asked writes nothing.</summary>
public sealed record ChangeRole(string ActorId, string ParticipantId, string Role, bool Give, DateTimeOffset? At = null) : ICommand<Participant>
{
    /// <summary>Whether <see cref="Prepare"/> judged the change allowed; <see cref="Run"/> keeps that judgement, since
    /// the change it made can move the participant to where the actor stands.</summary>
    private bool _judged;

    /// <summary>Whether <see cref="Prepare"/> gave or took the role, which <see cref="Run"/> records.</summary>
    private bool _changed;

    public async Task Prepare(CancellationToken ct)
    {
        if (await Judge(ct) is not { Refusal: null, Bag: { } bag }) return;
        _judged = true;
        if (bag.InRole(Role) == Give) return;
        var roles = AppHost.GetRequiredService<RoleCollection>(nameof(ChangeRole));
        if (Give) await roles.Add(Role, ParticipantId, ct);
        else await roles.Remove(Role, ParticipantId, ct);
        _changed = true;
    }

    public async Task<Outcome<Participant>> Run(Sequences sequences, CancellationToken ct)
    {
        if (!_judged)
        {
            var judged = await Judge(ct);
            return judged.Refusal is { } refusal ? new Outcome<Participant>.Refused(refusal) : new Outcome<Participant>.Done(judged.Target!);
        }

        if (await Participant.Get(ParticipantId, ct) is not { } target) return new Outcome<Participant>.Refused(People.NoOne);
        if (_changed)
        {
            await Event.Next(sequences, EventKind.RoleChanged, At ?? DateTimeOffset.UtcNow, actorId: ActorId, targetId: target.Id,
                role: Role, given: Give).Save(ct);
        }

        return new Outcome<Participant>.Done(target);
    }

    /// <summary>The judgement of <see cref="People.Judge"/>, then whether the role is one given here and whether the actor
    /// holds what it grants.</summary>
    private async Task<People.Judged> Judge(CancellationToken ct)
    {
        var judged = await People.Judge(ActorId, ParticipantId, Bit.ManageRoles, At ?? DateTimeOffset.UtcNow, ct);
        if (judged.Refusal is not null) return judged;
        if (People.Given.All(given => given.Key != Role)) return judged with { Refusal = People.NotARole };
        var role = await AppHost.GetRequiredService<RoleCollection>(nameof(ChangeRole)).Get(Role, ct);
        if (role is null) return judged with { Refusal = People.NotARole };
        return People.Holding(judged.Actor!, role) is { } refusal ? judged with { Refusal = refusal } : judged;
    }
}
