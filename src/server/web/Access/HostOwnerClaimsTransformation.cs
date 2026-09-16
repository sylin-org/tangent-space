using System.Security.Claims;
using Koan.Identity;
using Microsoft.AspNetCore.Authentication;
using Tangent.Identity;
using Tangent.Spaces;
using Tangent.Infrastructure;
using Tangent.Application;

namespace Tangent.Access;

/// <summary>Projects durable Tangent ownership into Koan's host-identity operator claim.</summary>
public sealed class HostOwnerClaimsTransformation : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.IsInRole(IdentityRoles.Operator)) return principal;
        var participantId = principal.FindFirst(ParticipationConstants.ParticipantClaim)?.Value;
        if (!Participant.IsValidId(participantId)) return principal;
        var space = await Space.Get(TangentConstants.SpaceId);
        if (space?.IsOwner(participantId) != true) return principal;
        var identity = principal.Identities.FirstOrDefault(value => value.IsAuthenticated);
        identity?.AddClaim(new Claim(identity.RoleClaimType, IdentityRoles.Operator));
        return principal;
    }
}
