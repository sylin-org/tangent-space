using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Participants;

namespace Tangent.Access;

/// <summary>Creates a role named <see cref="Name"/> that grants <see cref="Permissions"/>, keyed <c>role:</c> and the
/// word its name gives, a slug made unique among the roles with a numbered suffix, which never changes. It stands
/// directly above the member role, below every other, so it outranks no one until it is moved. The place exists for the
/// actor, who holds <see cref="Bit.ManageRoles"/> (<see cref="RoleSettings.Actor"/>); the name is a role's
/// (<see cref="RoleSettings.NameRule"/>); the actor holds every permission it grants (<see cref="People.NotHeld"/>) and
/// manages a role in that place (<see cref="RoleCollection.Manages"/>). The log records nothing.</summary>
public sealed record CreateRole(string ActorId, string Name, Bit Permissions) : ICommand<Role>
{
    public async Task<Outcome<Role>> Run(CancellationToken ct)
    {
        var (space, actor) = await RoleSettings.Actor(ActorId, ct);
        if (RoleSettings.Named(Name) is not { } name) throw new Refusal(RoleSettings.NameRule);
        if (!People.Holds(actor, Permissions)) throw new Refusal(People.NotHeld);
        var roles = RoleSettings.Collection();
        var key = await FreeKey(roles, name, ct) ?? throw new Refusal(RoleSettings.NoWord);
        var position = ((await roles.Get(Roles.Member, ct))?.Position ?? 0) + 1;
        if (ActorId != space.OperatorId && !await roles.Manages(actor.Bag, key, position, ct)) throw new Refusal(RoleSettings.NotManaged);
        return new Outcome<Role>.Done(await roles.Define(key, name, Bits.Tokens(Permissions), position: position, ct: ct));
    }

    /// <summary>The first key no role holds among the word <paramref name="name"/> gives and that word with a numbered
    /// suffix, up to <see cref="RoleSettings.SuffixBound"/>; null where each is held.</summary>
    private static async Task<string?> FreeKey(RoleCollection roles, string name, CancellationToken ct)
    {
        var word = Posts.Slug.From(name, RoleSettings.Unnamed);
        for (var suffix = 1; suffix <= RoleSettings.SuffixBound; suffix++)
        {
            var key = RoleTokens.RolePrefix + (suffix == 1 ? word : Posts.Slug.WithSuffix(word, suffix));
            if (await roles.Get(key, ct) is null) return key;
        }

        return null;
    }
}
