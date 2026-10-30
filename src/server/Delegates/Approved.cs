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
    /// <summary>The most approvals one page of the profile shows.</summary>
    public const int PageSize = 50;

    /// <summary>The highest page number <see cref="Of"/> reads, where the rows it asks Koan for would pass what a count
    /// holds.</summary>
    public const int LastPage = int.MaxValue / PageSize - 1;

    /// <summary>Page <paramref name="page"/>, from one, of the live approvals of <paramref name="personId"/>, newest
    /// first, and whether another page follows: Koan's read of the first <paramref name="page"/> pages and one more
    /// approval, since Koan reads a person's approvals from their newest and from no cursor.</summary>
    public static async Task<(IReadOnlyList<AgentGrant> Page, bool More)> Of(string personId, int page, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page, LastPage);
        var read = await AgentGrant.Approvals(personId, page * PageSize + 1, ct);
        return (read.Skip((page - 1) * PageSize).Take(PageSize).ToList(), read.Count > page * PageSize);
    }

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
