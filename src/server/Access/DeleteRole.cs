using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Participants;

namespace Tangent.Access;

/// <summary>Deletes the role keyed <see cref="Key"/>, which Koan does with its members and every Tangent's overwrite for
/// it, closing its place in the order. The place exists for the actor, who holds <see cref="Bit.ManageRoles"/>
/// (<see cref="RoleSettings.Actor"/>); the role is not a seeded one (<see cref="RoleSettings.SeededStays"/>), which every
/// start would give back; it exists and the actor manages it (<see cref="People.Manages"/>); and the actor holds every
/// permission it grants, since deleting it takes them from its members (<see cref="People.Holding"/>). It answers with the
/// role as it stood; the log records nothing.</summary>
public sealed record DeleteRole(string ActorId, string Key) : ICommand<Role>
{
    public async Task<Outcome<Role>> Run(CancellationToken ct)
    {
        var (space, actor) = await RoleSettings.Actor(ActorId, ct);
        if (Roles.Seeded(Key)) throw new Refusal(Key == Roles.Everyone ? RoleSettings.EveryoneStays : RoleSettings.SeededStays);
        var roles = RoleSettings.Collection();
        var role = await RoleSettings.Managed(roles, space, actor, ActorId, Key, ct);
        if (People.Holding(actor, role) is { } refusal) throw new Refusal(refusal);
        await roles.Delete(Key, ct);
        return new Outcome<Role>.Done(role);
    }
}
