using System.Security.Claims;
using Koan.Web.Auth.Connector.Atproto;

namespace Tangent.Identity;

public static class ParticipationAccess
{
    public static bool UsesCredential(ClaimsPrincipal principal) => principal.HasClaim(claim => claim.Type == ParticipationConstants.CredentialClaim);

    /// <summary>Credential grants limit transport operations; topic policy must still be checked afterward.
    /// The universal gate is the participant claim — every principal carries it. Holding an atproto
    /// identity is NOT a prerequisite for participation; atproto-specific flows keep their own inline
    /// DID checks with flow-specific honest outcomes: source writes and edits throw
    /// UnauthorizedAccessException, source readiness reports "unsupported" (never pending), and the
    /// connection endpoints answer 401/403 without a DID claim.</summary>
    public static string Require(ClaimsPrincipal principal, string grant)
    {
        var participant = principal.FindFirst(ParticipationConstants.ParticipantClaim)?.Value;
        if (principal.Identity?.IsAuthenticated != true || !Participant.IsValidId(participant))
            throw new UnauthorizedAccessException("A verified participant identity is required.");
        if (!ParticipationGrants.IsKnown(grant)) throw new ArgumentException("Unknown participation operation.", nameof(grant));
        if (UsesCredential(principal) && !principal.HasClaim(ParticipationConstants.GrantClaim, grant))
            throw new UnauthorizedAccessException("The participant credential does not permit this operation.");
        return participant!;
    }
}
