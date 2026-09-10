using System.Security.Claims;
using CarpaNet.Identity;
using Koan.Web.Auth.Connector.Atproto;

namespace TangentSpace.Participation;

public static class ParticipationAccess
{
    public static bool UsesCredential(ClaimsPrincipal principal) => principal.HasClaim(claim => claim.Type == ParticipationConstants.CredentialClaim);

    /// <summary>Credential grants limit transport operations; room policy must still be checked afterward.</summary>
    public static string Require(ClaimsPrincipal principal, string grant)
    {
        var did = principal.FindFirst(AtprotoClaimTypes.Did)?.Value;
        if (principal.Identity?.IsAuthenticated != true || did is null || !IdentityResolver.IsValidDid(did))
            throw new UnauthorizedAccessException("A verified participant identity is required.");
        if (!ParticipationGrants.IsKnown(grant)) throw new ArgumentException("Unknown participation operation.", nameof(grant));
        if (UsesCredential(principal) && !principal.HasClaim(ParticipationConstants.GrantClaim, grant))
            throw new UnauthorizedAccessException("The participant credential does not permit this operation.");
        return did;
    }

    internal static string EnrollmentDid(ClaimsPrincipal verifiedCookie)
    {
        if (UsesCredential(verifiedCookie)) throw new UnauthorizedAccessException("Enrollment requires a browser sign-in.");
        return Require(verifiedCookie, ParticipationGrants.Welcome);
    }
}
