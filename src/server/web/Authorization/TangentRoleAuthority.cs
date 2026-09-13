using Koan.Identity.Roles;
using TangentSpace.Communities;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Authorization;

/// <summary>Human Host ownership and scoped Tangent ownership are the roots of role administration.</summary>
public sealed class TangentRoleAuthority : IScopedRoleAuthorityContributor
{
    private static readonly IReadOnlySet<ScopedRoleAuthorityOperation> all = Enum.GetValues<ScopedRoleAuthorityOperation>().ToHashSet();

    public async ValueTask<IReadOnlyList<ScopedRoleAuthorityEnvelope>> Contribute(ScopedRoleAuthorityRequest request,
        CancellationToken ct = default)
    {
        if (!request.Actor.IsAuthenticated || request.Target.TenantId != TangentRoleScopes.Tenant) return [];
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
        => new(scope, all, Descendants: true, AllowSelfAssignment: true,
            ProofKey: $"{kind}:{owner}:{scope.Id}", ProofVersion: version);
}
