using Koan.Data.Core.Model;
using TangentSpace.Infrastructure;

namespace TangentSpace.Site;

public sealed class TangentSite : Entity<TangentSite>
{
    public string Name { get; set; } = "";
    public string OwnerDid { get; set; } = "";
    public DateTimeOffset EstablishedAt { get; set; }
    public long PolicyRevision { get; set; }

    public static TangentSite Establish(SiteOptions options, string verifiedDid, DateTimeOffset now)
    {
        if (!string.Equals(options.OwnerDid, verifiedDid, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the explicitly configured account can establish this site.");
        return new TangentSite { Id = TangentConstants.SiteId, Name = options.Name.Trim(), OwnerDid = verifiedDid,
            EstablishedAt = now, PolicyRevision = 1 };
    }

    public void CheckConfiguredOwner(SiteOptions options)
    {
        if (!string.Equals(OwnerDid, options.OwnerDid, StringComparison.Ordinal))
            throw new InvalidOperationException("Tangent:Site:OwnerDid differs from the persisted owner. Restore the configured DID; changing configuration cannot transfer site ownership.");
    }

    public bool IsOwner(string? verifiedDid) => string.Equals(OwnerDid, verifiedDid, StringComparison.Ordinal);
}
