using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>Sets what the role keyed <see cref="RoleKey"/> holds in the Tangent <see cref="Slug"/> beyond what it holds in
/// the place (<see cref="RoleAccess"/>), as the role's overwrite there. The Tangent exists for the actor, who holds
/// <see cref="Bit.View"/> on it, or the command answers that no Tangent has the slug; the actor may manage it
/// (<see cref="Bit.ManageTangent"/>, which <see cref="Bit.ManageSpace"/> implies), or is refused with the gate word; the moderators' Tangent keeps its access
/// (<see cref="ChangeTangent.Fixed"/>); the role is one whose access is set here, neither everyone, whose access is the
/// Tangent's audience (<see cref="EveryoneReads"/>), nor one that administers, which reads and posts everywhere
/// (<see cref="Administers"/>); and the actor manages it (<see cref="People.Manages"/>). The log records nothing, since the
/// Tangent's audience stays as it was.</summary>
public sealed record ChangeAccess(string Slug, string ActorId, string RoleKey, RoleAccess Access) : ICommand<Tangent>
{
    /// <summary>The answer to a change of the access of a role that administers.</summary>
    public static readonly Reason Administers = Reason.Invalid("An administrator reads and posts in every Tangent; set the access of another role.");

    /// <summary>The answer to a change of everyone's access, which is the Tangent's audience.</summary>
    public static readonly Reason EveryoneReads = Reason.Invalid("Everyone reads as the Tangent’s audience says: make it public or private in its settings.");

    public async Task<Outcome<Tangent>> Run(CancellationToken ct)
    {
        if (await Tangent.Get(Slug, ct) is not { } tangent) return new Outcome<Tangent>.Refused(ChangeTangent.NoTangent);
        var capabilities = await Capabilities.Load(ActorId, Slug, null, ct);
        if (!capabilities.Exists) return new Outcome<Tangent>.Refused(ChangeTangent.NoTangent);
        if (capabilities.Gate(Bit.ManageTangent) is { } gate) return new Outcome<Tangent>.Refused(gate);
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return new Outcome<Tangent>.Refused(ChangeTangent.NoTangent);
        if (space.ModeratorsTangentId == Slug) return new Outcome<Tangent>.Refused(ChangeTangent.Fixed);
        if (RoleKey == Roles.Everyone) return new Outcome<Tangent>.Refused(EveryoneReads);
        var roles = RoleSettings.Collection();
        if (await roles.Get(RoleKey, ct) is not { } role) return new Outcome<Tangent>.Refused(RoleSettings.NoRole);
        if (role.Permissions.Contains(RoleTokens.Administrator, StringComparer.Ordinal)) return new Outcome<Tangent>.Refused(Administers);
        var place = await Capabilities.Load(ActorId, null, null, ct);
        if (!People.Manages(place, ActorId, space, role)) return new Outcome<Tangent>.Refused(RoleSettings.NotManaged);
        await Overwrites.SetAccess(roles, Slug, RoleKey, Access, ct);
        return new Outcome<Tangent>.Done(tangent);
    }
}
