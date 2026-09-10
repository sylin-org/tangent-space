using Koan.Data.Core.Model;
using CarpaNet.Identity;
using TangentSpace.Infrastructure;

namespace TangentSpace.Site;

public sealed class TangentSite : Entity<TangentSite>
{
    public string Name { get; set; } = "";
    public string OwnerDid { get; set; } = "";
    public DateTimeOffset EstablishedAt { get; set; }
    public long PolicyRevision { get; set; }
    public string WelcomeMessage { get; set; } = "";
    public string Byline { get; set; } = "";
    public string CoverImageUrl { get; set; } = "";
    public string Motd { get; set; } = "";
    public string CreationPolicy { get; set; } = "owner_only";
    public bool AllowAgentTangentOwnership { get; set; }
    public bool HumanDeclared { get; set; }
    public string BackgroundScene { get; set; } = "galaxy";
    public string BackgroundColor { get; set; } = "";
    public int BackgroundIntensity { get; set; } = 35;
    public bool BackgroundMotion { get; set; } = true;
    public bool BackgroundMouseSpotlight { get; set; } = true;

    public static TangentSite Establish(SiteOptions options, string verifiedDid, DateTimeOffset now)
    {
        if (!IdentityResolver.IsValidDid(verifiedDid))
            throw new ArgumentException("A verified AT DID is required.", nameof(verifiedDid));
        if (!string.IsNullOrWhiteSpace(options.OwnerDid) && !string.Equals(options.OwnerDid, verifiedDid, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the explicitly configured account can establish this site.");
        return new TangentSite { Id = TangentConstants.SiteId, Name = options.Name.Trim(), OwnerDid = verifiedDid,
            EstablishedAt = now, PolicyRevision = 1, WelcomeMessage = options.WelcomeMessage, Motd = options.Motd,
            CreationPolicy = options.CreationPolicy, AllowAgentTangentOwnership = options.AllowAgentTangentOwnership,
            HumanDeclared = false };
    }

    public void CheckConfiguredOwner(SiteOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.OwnerDid) && !string.Equals(OwnerDid, options.OwnerDid, StringComparison.Ordinal))
            throw new InvalidOperationException("Tangent:Site:OwnerDid differs from the persisted owner. Restore the configured DID; changing configuration cannot transfer site ownership.");
    }

    public bool CanCreateTangent(TangentSpace.Participants.Participant? actor)
        => actor is not null && !actor.IsSuspended && (IsOwner(actor.Id) || CreationPolicy == "everyone"
            || CreationPolicy == "humans" && actor.Classification == TangentSpace.Participants.ParticipantClassification.Human);

    public bool IsOwner(string? verifiedDid) => string.Equals(OwnerDid, verifiedDid, StringComparison.Ordinal);
}
