using Koan.Identity.Roles;
using TangentSpace.Communities;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Authorization;

/// <summary>Human Host ownership and scoped Tangent ownership are the roots of role administration.</summary>
public sealed class TangentRoleAuthority : IScopedRoleAuthorityContributor
{
    private static readonly IReadOnlySet<ScopedRoleAuthorityOperation> all = Enum.GetValues<ScopedRoleAuthorityOperation>().ToHashSet();
    private const string OwnerSuffix = ":owner";

    public async ValueTask<IReadOnlyList<ScopedRoleAuthorityEnvelope>> Contribute(ScopedRoleAuthorityRequest request,
        CancellationToken ct = default)
    {
        if (!request.Actor.IsAuthenticated || request.Target.TenantId != TangentRoleScopes.Tenant) return [];

        if (request.Operation == ScopedRoleAuthorityOperation.RemoveMember && IsOwnerRoleId(request.RoleId))
            return [];

        if (request.Operation == ScopedRoleAuthorityOperation.AddMember &&
            IsOwnerRoleId(request.RoleId, out var ownerScope) &&
            !await IsCanonicalOwner(request.Subject, ownerScope, ct).ConfigureAwait(false))
            return [];

        var site = await TangentSite.Get("site", ct);
        if (site?.IsOwner(request.Actor.Subject) == true)
            return [Envelope(TangentRoleScopes.HostScope, site.OwnerParticipantId, site.PolicyRevision, "host")];

        var tangentKey = request.Target.Type switch
        {
            TangentRoleScopes.Tangent => request.Target.Id,
            TangentRoleScopes.Topic => (await Room.Get(request.Target.Id, ct))?.TangentKey,
            _ => null
        };
        var tangent = tangentKey is null ? null : await TangentCommunity.Get(tangentKey, ct);
        return tangent?.IsOwner(request.Actor.Subject) == true
            ? [Envelope(TangentRoleScopes.TangentScope(tangent.Id), tangent.OwnerParticipantId, tangent.PolicyRevision, "tangent")]
            : [];
    }

    public async ValueTask<bool> Validate(ScopedRoleAuthorityRequest request, ScopedRoleAuthorityEnvelope envelope,
        CancellationToken ct = default)
    {
        if (envelope.ProofKey is null || envelope.ProofVersion is null) return false;
        var parts = envelope.ProofKey.Split(':', 3);
        if (parts.Length != 3 || parts[1] != request.Actor.Subject) return false;
        if (parts[0] == "host")
        {
            var site = await TangentSite.Get("site", ct);
            return site?.IsOwner(parts[1]) == true && site.PolicyRevision == envelope.ProofVersion;
        }
        if (parts[0] == "tangent")
        {
            var tangent = await TangentCommunity.Get(parts[2], ct);
            return tangent?.IsOwner(parts[1]) == true && tangent.PolicyRevision == envelope.ProofVersion;
        }
        return false;
    }

    private static ScopedRoleAuthorityEnvelope Envelope(ScopedRoleScopeRef scope, string owner, long version, string kind)
        => new(scope, all, Descendants: true, AllowSelfMembership: true,
            ProofKey: $"{kind}:{owner}:{scope.Id}", ProofVersion: version);

    private static bool IsOwnerRoleId(string? roleId)
        => IsOwnerRoleId(roleId, out _);

    private static bool IsOwnerRoleId(string? roleId, out ScopedRoleScopeRef scope)
    {
        scope = null!;
        if (string.IsNullOrWhiteSpace(roleId) || !roleId.EndsWith(OwnerSuffix, StringComparison.Ordinal))
            return false;
        var core = roleId[..^OwnerSuffix.Length];
        var parts = core.Split(':');
        if (parts.Length != 3 || parts[0] != "tangent") return false;
        scope = new(TangentRoleScopes.Tenant, parts[1], parts[2]);
        return true;
    }

    private static async ValueTask<bool> IsCanonicalOwner(string? subject, ScopedRoleScopeRef ownerScope, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(subject)) return false;
        return ownerScope.Type switch
        {
            TangentRoleScopes.Host => (await TangentSite.Get("site", ct))?.IsOwner(subject) == true,
            TangentRoleScopes.Tangent => (await TangentCommunity.Get(ownerScope.Id, ct))?.IsOwner(subject) == true,
            _ => false,
        };
    }
}
