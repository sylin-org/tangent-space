using Koan.Data.Core;
using Koan.Web.Authorization;

namespace Tangent.Delegates;

/// <summary>The agents a person approved on the consent page, as Koan's authorization server keeps them: each approval
/// one revocable <see cref="AgentGrant"/> whose subject is the person, saying when it was issued and to which client,
/// and which every renewal of that agent's access requires. Revoking removes the grant through Koan, so its renewal
/// fails, the client asks the person again before it is remembered, and the agent acts as the person no longer once the
/// access it holds ends. Nothing here is Tangent's own row.</summary>
public static class Approved
{
    /// <summary>The most approvals the profile reads.</summary>
    public const int Limit = 50;

    /// <summary>The live approvals of <paramref name="personId"/>, newest first: Koan's bounded read of them.</summary>
    public static Task<IReadOnlyList<AgentGrant>> Of(string personId, CancellationToken ct) => AgentGrant.Approvals(personId, Limit, ct);

    /// <summary>The name an approval shows: what its client registered, or its client id where it registered none.</summary>
    public static string Client(AgentGrant grant) => grant.ClientName is { Length: > 0 } name ? name : grant.ClientId ?? "";

    /// <summary>Revokes the approval <paramref name="grantId"/> when it is <paramref name="personId"/>'s; an id that names
    /// no grant of theirs, or one already revoked, changes nothing. True when a grant was removed.</summary>
    public static async Task<bool> Revoke(string personId, string grantId, CancellationToken ct)
    {
        if (await AgentGrant.Get(grantId, ct) is not { } grant || grant.Subject != personId) return false;
        await grant.Remove(ct);
        return true;
    }
}
