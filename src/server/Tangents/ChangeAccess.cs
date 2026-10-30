using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Tangents;

/// <summary>Sets what the role keyed <see cref="RoleKey"/> holds in the Tangent <see cref="Slug"/> beyond what it holds in
/// the place (<see cref="RoleAccess"/>), as the role's overwrite there. The Tangent exists for the actor, who holds
/// <see cref="Bit.View"/> on it, or the command answers that no Tangent has the slug; the actor may manage it
/// (<see cref="TangentWrites.ManageGate"/>), or is refused with the gate word; the moderators' Tangent keeps its access
/// (<see cref="ChangeTangent.Fixed"/>); the role is one whose access is set here, neither everyone, whose access is the
/// Tangent's audience, nor one that administers, which reads and posts everywhere (<see cref="Administers"/>); and the
/// actor manages it (<see cref="People.Manages"/>). Koan commits an overwrite only outside a transaction, so
/// <see cref="Prepare"/> judges and writes it before the transaction opens, and <see cref="Run"/> answers with the
/// Tangent; the log records nothing, since the Tangent's audience stays as it was.</summary>
public sealed record ChangeAccess(string Slug, string ActorId, string RoleKey, RoleAccess Access) : ICommand<Tangent>
{
    /// <summary>The answer to a change of the access of a role that administers, or of everyone.</summary>
    public const string Administers = "An administrator reads and posts in every Tangent, and everyone reads what its audience says; set the access of another role.";

    private Tangent? _tangent;

    public async Task Prepare(CancellationToken ct)
    {
        if (await Tangent.Get(Slug, ct) is not { } tangent) throw new Refusal(ChangeTangent.NoTangent);
        var capabilities = await Capabilities.Load(ActorId, Slug, null, ct);
        if (!capabilities.Exists) throw new Refusal(ChangeTangent.NoTangent);
        if (TangentWrites.ManageGate(capabilities) is { } gate) throw new Refusal(gate);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new Refusal(ChangeTangent.NoTangent);
        if (space.ModeratorsTangentId == Slug) throw new Refusal(ChangeTangent.Fixed);
        if (RoleKey == Roles.Everyone) throw new Refusal(Administers);
        var roles = RoleSettings.Collection();
        if (await roles.Get(RoleKey, ct) is not { } role) throw new Refusal(RoleSettings.NoRole);
        if (role.Permissions.Contains(RoleTokens.Administrator, StringComparer.Ordinal)) throw new Refusal(Administers);
        var place = await Capabilities.Load(ActorId, null, null, ct);
        if (!People.Manages(place, ActorId, space, role)) throw new Refusal(RoleSettings.NotManaged);
        await Overwrites.SetAccess(roles, Slug, RoleKey, Access, ct);
        _tangent = tangent;
    }

    public Task<Outcome<Tangent>> Run(Sequences sequences, CancellationToken ct)
        => Task.FromResult<Outcome<Tangent>>(new Outcome<Tangent>.Done(_tangent ?? throw new InvalidOperationException("Access is set in its command's Prepare.")));
}
