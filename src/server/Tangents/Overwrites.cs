using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>A Tangent's overwrites: Koan's <see cref="PermissionOverwrite"/> rows on the resource <see cref="Resource"/>
/// names, written through <see cref="RoleCollection"/>, read for evaluation from its cache and one row at a time by key
/// (<see cref="Of"/>). A Tangent's audience is nothing more than <see cref="Bit.View"/> in its <c>everyone</c> overwrite.
/// Koan commits an overwrite at once and refuses one inside the writer's transaction, so every write here happens outside
/// it.</summary>
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

    /// <summary>The overwrite of the role <paramref name="roleKey"/> on a Tangent's resource, one keyed read; null where
    /// there is none.</summary>
    public static Task<PermissionOverwrite?> Of(string tangentId, string roleKey, CancellationToken ct)
        => PermissionOverwrite.Get(PermissionOverwrite.KeyFor(Resource(tangentId), OverwriteSubjectKind.Role, roleKey), ct);
}
