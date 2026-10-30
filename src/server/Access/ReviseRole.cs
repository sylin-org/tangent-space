using Koan.Identity.Roles;
using Tangent.Participants;
using Tangent.Writer;

namespace Tangent.Access;

/// <summary>Which way a role moves in Koan's order: one place higher or one place lower.</summary>
public enum RoleMove
{
    Higher,
    Lower
}

/// <summary>Changes the role keyed <see cref="Key"/>: renames it, sets what it grants, and moves it one place higher or
/// lower in the order, each left as it is where null; its key never changes. The place exists for the actor, who holds
/// <see cref="Bit.ManageRoles"/> (<see cref="RoleSettings.Actor"/>); the role exists and the actor manages it, and may
/// move it where it goes (<see cref="People.Manages"/>); everyone stays as it is (<see cref="RoleSettings.EveryoneStays"/>);
/// the name is a role's; the actor holds each permission given or taken away (<see cref="People.NotHeld"/>); and the
/// administrator role keeps <see cref="Bit.Administrator"/>. Koan's role writes are not part of a transaction, so
/// <see cref="Prepare"/> judges and writes them before the transaction opens, and <see cref="Run"/> answers with the role
/// as it then stands; the log records nothing. A move past either end of the order stays where it is.</summary>
public sealed record ReviseRole(string ActorId, string Key, string? Name = null, Bit? Permissions = null, RoleMove? Move = null) : ICommand<Role>
{
    private Role? _revised;

    public async Task Prepare(CancellationToken ct)
    {
        var (space, actor) = await RoleSettings.Actor(ActorId, ct);
        if (Key == Roles.Everyone) throw new Refusal(RoleSettings.EveryoneStays);
        var roles = RoleSettings.Collection();
        var role = await RoleSettings.Managed(roles, space, actor, ActorId, Key, ct);
        string? name = null;
        if (Name is not null && (name = RoleSettings.Named(Name)) is null) throw new Refusal(RoleSettings.NameRule);
        var granted = Bits.InTokens(role.Permissions);
        if (Permissions is { } wanted)
        {
            if (!People.Holds(actor, wanted ^ granted)) throw new Refusal(People.NotHeld);
            if (Key == Roles.Administrator && !wanted.HasFlag(Bit.Administrator)) throw new Refusal(RoleSettings.Administers);
        }

        int? to = Move switch
        {
            RoleMove.Higher => role.Position + 1,
            RoleMove.Lower => Math.Max(role.Position - 1, 1),
            _ => null
        };
        if (to is { } position && !People.Manages(actor, ActorId, space, role, position)) throw new Refusal(RoleSettings.NotManaged);

        if (name is not null && name != role.Name) role = await roles.Rename(Key, name, ct);
        // Only the bits are rewritten: a token of the role's that is no bit stays as it is.
        if (Permissions is { } bits && bits != granted)
            role = await roles.SetPermissions(Key, [.. role.Permissions.Where(token => Bits.InTokens([token]) == Bit.None), .. Bits.Tokens(bits)], ct);
        if (to is { } target && target != role.Position) role = await roles.Move(Key, target, ct);
        _revised = role;
    }

    public Task<Outcome<Role>> Run(Sequences sequences, CancellationToken ct)
        => Task.FromResult<Outcome<Role>>(new Outcome<Role>.Done(_revised ?? throw new InvalidOperationException("A role is changed in its command's Prepare.")));
}
