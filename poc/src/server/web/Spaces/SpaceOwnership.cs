using System.Security.Claims;
using Tangent.Identity;
using Tangent.Infrastructure;

namespace Tangent.Spaces;

/// <summary>The owner check, stated once. Two controllers had grown private copies of it, and a
/// rule duplicated is a rule that drifts: both must ask for the management grant and both must
/// read ownership from the Space, never from a role bag or a claim.</summary>
public static class SpaceOwnership
{
    /// <summary>True only for the Space's accountable owner. Answers false rather than throwing
    /// when the caller holds no management grant, so callers can return their own refusal.</summary>
    public static async Task<bool> IsOwner(ClaimsPrincipal principal, CancellationToken ct)
    {
        string actor;
        try { actor = ParticipationAccess.Require(principal, ParticipationGrants.Manage); }
        catch (UnauthorizedAccessException) { return false; }
        return (await Space.Get(TangentConstants.SpaceId, ct))?.IsOwner(actor) == true;
    }
}
