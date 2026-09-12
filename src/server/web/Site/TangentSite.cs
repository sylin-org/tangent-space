using CarpaNet.Identity;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;

namespace TangentSpace.Site;

/// <summary>The site row keys its owner by participant id. The configured Tangent:Site:OwnerDid
/// remains an atproto DID pin: only the explicitly configured account — verified by an atproto
/// browser sign-in — can establish the site, and only that DID's current holder is the owner.</summary>
public sealed class TangentSite : Entity<TangentSite>
{
    public string Name { get; set; } = "";
    public string OwnerParticipantId { get; set; } = "";
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

    /// <summary>Establishment is the one atproto-DID gate on this row: the verified sign-in DID
    /// must satisfy the configured pin, and its current holder becomes the owner participant.</summary>
    public static TangentSite Establish(SiteOptions options, string verifiedDid, string ownerParticipantId, DateTimeOffset now)
    {
        if (!IdentityResolver.IsValidDid(verifiedDid))
            throw new ArgumentException("A verified AT DID is required.", nameof(verifiedDid));
        if (!Participant.IsValidId(ownerParticipantId))
            throw new ArgumentException("The site owner must be an enrolled participant.", nameof(ownerParticipantId));
        if (!string.IsNullOrWhiteSpace(options.OwnerDid) && !string.Equals(options.OwnerDid, verifiedDid, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the explicitly configured account can establish this site.");
        return new TangentSite { Id = TangentConstants.SiteId, Name = options.Name.Trim(), OwnerParticipantId = ownerParticipantId,
            EstablishedAt = now, PolicyRevision = 1, WelcomeMessage = options.WelcomeMessage, Motd = options.Motd,
            CreationPolicy = options.CreationPolicy, AllowAgentTangentOwnership = options.AllowAgentTangentOwnership,
            HumanDeclared = false };
    }

    public async Task CheckConfiguredOwner(SiteOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.OwnerDid)) return;
        var identity = await ParticipantIdentity.Get(ParticipantIdentity.AtprotoKey(options.OwnerDid), ct);
        if (identity is null || identity.ParticipantId != OwnerParticipantId)
            throw new InvalidOperationException("Tangent:Site:OwnerDid differs from the persisted owner. Restore the configured DID; changing configuration cannot transfer site ownership.");
    }

    public bool CanCreateTangent(TangentSpace.Participants.Participant? actor)
        => actor is not null && !actor.IsSuspended && (IsOwner(actor.Id) || CreationPolicy == "everyone"
            || CreationPolicy == "humans" && actor.Classification == TangentSpace.Participants.ParticipantClassification.Human);

    public bool IsOwner(string? participantId) => string.Equals(OwnerParticipantId, participantId, StringComparison.Ordinal);
}
