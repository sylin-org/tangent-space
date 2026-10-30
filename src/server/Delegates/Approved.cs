using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Web.Authorization;

namespace Tangent.Delegates;

/// <summary>The agents a person approved on the consent page, as Koan's authorization server keeps them: one revocable
/// <see cref="AgentGrant"/> per approval, whose subject is the person, which every renewal of the agent's access
/// requires. Revoking removes the grant through Koan, so the agent's next renewal fails and it acts as the person no
/// longer once the access it holds ends. Nothing here is Tangent's own row.</summary>
public static class Approved
{
    /// <summary>The most approvals the profile reads.</summary>
    public const int Limit = 50;

    /// <summary>The live approvals of <paramref name="personId"/>, newest first: one bounded read of Koan's grants by
    /// subject.</summary>
    public static async Task<IReadOnlyList<AgentGrant>> Of(string personId, DateTimeOffset now, CancellationToken ct)
    {
        var grants = await AgentGrant.Query(grant => grant.Subject == personId, new QueryDefinition { Page = 1, PageSize = Limit }, ct);
        return grants.Where(grant => grant.IsActive(now)).OrderByDescending(grant => At(grant) ?? DateTimeOffset.MinValue)
            .ThenByDescending(grant => grant.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Revokes the approval <paramref name="grantId"/> when it is <paramref name="personId"/>'s; an id that names
    /// no grant of theirs, or one already revoked, changes nothing. True when a grant was removed.</summary>
    public static async Task<bool> Revoke(string personId, string grantId, CancellationToken ct)
    {
        if (await AgentGrant.Get(grantId, ct) is not { } grant || grant.Subject != personId) return false;
        await grant.Remove(ct);
        return true;
    }

    /// <summary>When the person approved the grant: the time its id carries, since Koan keys a grant by a version 7
    /// UUID minted when the approval created it (RFC 9562), and records no time of its own. Null for an id of another
    /// shape.</summary>
    public static DateTimeOffset? At(AgentGrant grant)
    {
        if (!Guid.TryParse(grant.Id, out var id) || id.Version != 7) return null;
        return DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(id.ToString("N")[..12], 16));
    }
}
