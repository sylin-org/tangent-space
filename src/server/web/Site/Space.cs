using CarpaNet.Identity;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Authorization;

namespace TangentSpace.Site;

/// <summary>The space row keys its owner by participant id. The configured Tangent:Space:OwnerDid
/// remains an atproto DID pin: only the explicitly configured account — verified by an atproto
/// browser sign-in — can establish the space, and only that DID's current holder is the owner.</summary>
public sealed class Space : Entity<Space>
{
    public string Name { get; set; } = "";
    public string OwnerParticipantId { get; set; } = "";
    public DateTimeOffset EstablishedAt { get; set; }
    public long PolicyRevision { get; set; }
    public string WelcomeMessage { get; set; } = "";
    public string Byline { get; set; } = "";
    public string CoverImageUrl { get; set; } = "";
    public string Motd { get; set; } = "";
    public bool AllowAgentTangentOwnership { get; set; }
    public bool HumanDeclared { get; set; }
    public string BackgroundScene { get; set; } = "galaxy";
    public string BackgroundColor { get; set; } = "";
    public int BackgroundIntensity { get; set; } = 35;
    public bool BackgroundMotion { get; set; } = true;
    public bool BackgroundMouseSpotlight { get; set; } = true;
    public AccessMap Access { get; set; } = AccessMap.ServerDefaults();

    /// <summary>Establishment is the one atproto-DID gate on this row: the verified sign-in DID
    /// must satisfy the configured pin, and its current holder becomes the owner participant.</summary>
    public static Space Establish(SpaceOptions options, string verifiedDid, string ownerParticipantId, DateTimeOffset now)
    {
        if (!IdentityResolver.IsValidDid(verifiedDid))
            throw new ArgumentException("A verified AT DID is required.", nameof(verifiedDid));
        if (!Participant.IsValidId(ownerParticipantId))
            throw new ArgumentException("The space owner must be an enrolled participant.", nameof(ownerParticipantId));
        if (!string.IsNullOrWhiteSpace(options.OwnerDid) && !string.Equals(options.OwnerDid, verifiedDid, StringComparison.Ordinal))
            throw new InvalidOperationException("Only the explicitly configured account can establish this space.");
        return new Space { Id = TangentConstants.SpaceId, Name = options.Name.Trim(), OwnerParticipantId = ownerParticipantId,
            EstablishedAt = now, PolicyRevision = 1, WelcomeMessage = options.WelcomeMessage, Motd = options.Motd,
            AllowAgentTangentOwnership = options.AllowAgentTangentOwnership,
            HumanDeclared = false };
    }

    public async Task CheckConfiguredOwner(SpaceOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.OwnerDid)) return;
        var identity = await ParticipantIdentity.Get(ParticipantIdentity.AtprotoKey(options.OwnerDid), ct);
        if (identity is null || identity.ParticipantId != OwnerParticipantId)
            throw new InvalidOperationException("Tangent:Space:OwnerDid differs from the persisted owner. Restore the configured DID; changing configuration cannot transfer space ownership.");
    }

    public bool IsOwner(string? participantId) => string.Equals(OwnerParticipantId, participantId, StringComparison.Ordinal);

    public void ChangeAccess(string actorId, AccessMap access, bool canManage = false)
    {
        if (!IsOwner(actorId) && !canManage) throw new UnauthorizedAccessException("The current role cannot change server access.");
        Access = (access ?? throw new ArgumentNullException(nameof(access))).NormalizeForServer();
        PolicyRevision = checked(PolicyRevision + 1);
    }
}
