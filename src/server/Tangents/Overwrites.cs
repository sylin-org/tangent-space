using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>What a role holds in one Tangent beyond what it holds in the place: nothing more (<see cref="Default"/>, no
/// overwrite), reading and nothing else (<see cref="Read"/>: <see cref="Bit.View"/> allowed, <see cref="Bit.Post"/> and
/// <see cref="Bit.Topic"/> denied), or reading and posting (<see cref="Post"/>: all three allowed). A private Tangent
/// opened to a role that may post is a members-only Tangent when the role is the member role.</summary>
public enum RoleAccess
{
    Default,
    Read,
    Post
}

/// <summary>A Tangent's overwrites: Koan's <see cref="PermissionOverwrite"/> rows on the resource <see cref="Resource"/>
/// names, written through <see cref="RoleCollection"/>, read for evaluation from its cache and one row at a time by key
/// (<see cref="Of"/>). A Tangent's audience is nothing more than <see cref="Bit.View"/> in its <c>everyone</c> overwrite,
/// and a role's access there (<see cref="RoleAccess"/>) its own overwrite. Koan commits an overwrite at once and refuses
/// one inside the writer's transaction, so every write here happens outside it.</summary>
public static class Overwrites
{
    /// <summary>What a Tangent's overwrites are written on: this, followed by its slug.</summary>
    public const string ResourcePrefix = "tangent:";

    /// <summary>The resource a Tangent's overwrites are written on.</summary>
    public static string Resource(string tangentId) => ResourcePrefix + tangentId;

    /// <summary>Writes the <c>everyone</c> overwrite that gives a Tangent this audience: <see cref="Bit.View"/>
    /// allowed, or denied.</summary>
    public static Task<PermissionOverwrite> SetAudience(RoleCollection roles, string tangentId, Audience audience, CancellationToken ct)
    {
        IReadOnlyList<string> view = [Bits.Token(Bit.View)];
        return roles.Overwrite(Resource(tangentId), OverwriteSubjectKind.Role, RoleTokens.Everyone,
            audience == Audience.Public ? view : [], audience == Audience.Public ? [] : view, ct: ct);
    }

    /// <summary>The audience a Tangent's <c>everyone</c> overwrite states, one keyed read of that row: public where it
    /// allows <see cref="Bit.View"/>, private where it denies it and does not allow it; null where it says nothing of
    /// <see cref="Bit.View"/> or there is none.</summary>
    public static async Task<Audience?> AudienceOf(string tangentId, CancellationToken ct)
    {
        var view = Bits.Token(Bit.View);
        var everyone = await Of(tangentId, RoleTokens.Everyone, ct);
        if (everyone is null) return null;
        if (everyone.Allow.Contains(view)) return Audience.Public;
        return everyone.Deny.Contains(view) ? Audience.Private : null;
    }

    /// <summary>Writes the overwrite of the role <paramref name="roleKey"/> on a Tangent that gives it this access, or
    /// removes it for <see cref="RoleAccess.Default"/>.</summary>
    public static async Task SetAccess(RoleCollection roles, string tangentId, string roleKey, RoleAccess access, CancellationToken ct)
    {
        IReadOnlyList<string> posting = Bits.Tokens(Bit.Post | Bit.Topic);
        switch (access)
        {
            case RoleAccess.Read:
                await roles.Overwrite(Resource(tangentId), OverwriteSubjectKind.Role, roleKey, Bits.Tokens(Bit.View), posting, ct: ct);
                break;
            case RoleAccess.Post:
                await roles.Overwrite(Resource(tangentId), OverwriteSubjectKind.Role, roleKey, Bits.Tokens(Bit.View | Bit.Post | Bit.Topic), [], ct: ct);
                break;
            default:
                await roles.RemoveOverwrite(Resource(tangentId), OverwriteSubjectKind.Role, roleKey, ct);
                break;
        }
    }

    /// <summary>The access a role's overwrite on a Tangent gives it: <see cref="RoleAccess.Post"/> where it allows
    /// <see cref="Bit.View"/> and <see cref="Bit.Post"/>, <see cref="RoleAccess.Read"/> where it allows
    /// <see cref="Bit.View"/> alone, <see cref="RoleAccess.Default"/> otherwise and where there is none.</summary>
    public static RoleAccess AccessOf(PermissionOverwrite? overwrite)
    {
        var allowed = overwrite is null ? Bit.None : Bits.InTokens(overwrite.Allow);
        return !allowed.HasFlag(Bit.View) ? RoleAccess.Default : allowed.HasFlag(Bit.Post) ? RoleAccess.Post : RoleAccess.Read;
    }

    /// <summary>The overwrite of the role <paramref name="roleKey"/> on a Tangent's resource, one keyed read; null where
    /// there is none.</summary>
    public static Task<PermissionOverwrite?> Of(string tangentId, string roleKey, CancellationToken ct)
        => PermissionOverwrite.Get(PermissionOverwrite.KeyFor(Resource(tangentId), OverwriteSubjectKind.Role, roleKey), ct);
}
