using CarpaNet.Identity;
using Koan.Data.Core.Model;
using TangentSpace.Infrastructure;
using TangentSpace.Site;

namespace TangentSpace.Communities;

/// <summary>A durable, independently owned community within this host.</summary>
public sealed class TangentCommunity : Entity<TangentCommunity>
{
    public const string HomeKey = "home";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Motto { get; set; } = "";
    public string Accent { get; set; } = "";
    public string Artwork { get; set; } = "";
    public string OwnerDid { get; set; } = "";
    public bool OpenToSignedIn { get; set; }
    // Only used by the host-owned home record to make the flat-room migration resumable.
    public bool LegacyRoomsAssigned { get; set; }
    public bool SetupComplete { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long PolicyRevision { get; set; }

    public static TangentCommunity Create(TangentSite? site, string actorDid, string key, string name, string? description,
        string? motto, string? accent, string? artwork, DateTimeOffset now)
    {
        if (site is null || !site.IsOwner(actorDid) || !IdentityResolver.IsValidDid(actorDid))
            throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the persisted site owner can create a Tangent.");
        CheckKey(key); CheckCard(name, description, motto, accent, artwork);
        return new TangentCommunity
        {
            Id = key, Name = name.Trim(), Description = Clean(description), Motto = Clean(motto), Accent = Clean(accent), Artwork = Clean(artwork),
            OwnerDid = actorDid, CreatedAt = now, UpdatedAt = now, PolicyRevision = 1, SetupComplete = true
        };
    }

    internal static TangentCommunity Home(TangentSite site, DateTimeOffset now) => new()
    {
        Id = HomeKey, Name = site.Name, Description = "", Motto = "", Accent = "", Artwork = "", OwnerDid = site.OwnerDid,
        OpenToSignedIn = true, CreatedAt = site.EstablishedAt == default ? now : site.EstablishedAt, UpdatedAt = now, PolicyRevision = 1
    };

    public void Change(string actorDid, string? name, string? description, string? motto, string? accent, string? artwork, DateTimeOffset now)
    {
        if (!IsOwner(actorDid)) throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the Tangent owner can change its card.");
        CheckCard(name ?? Name, description ?? Description, motto ?? Motto, accent ?? Accent, artwork ?? Artwork);
        Name = (name ?? Name).Trim(); Description = Clean(description ?? Description); Motto = Clean(motto ?? Motto);
        Accent = Clean(accent ?? Accent); Artwork = Clean(artwork ?? Artwork); UpdatedAt = now; PolicyRevision = checked(PolicyRevision + 1);
        SetupComplete = true;
    }

    public bool IsOwner(string? did) => string.Equals(OwnerDid, did, StringComparison.Ordinal);

    public bool CanParticipate(string? did, TangentMembership? membership)
    {
        if (membership is not null && (membership.TangentKey != Id || membership.ParticipantDid != did
            || membership.Id != TangentMembership.Key(Id, did ?? "") || !Enum.IsDefined(membership.Role)))
            throw new TangentRuleViolation(TangentDenial.MembershipMismatch, "The community membership does not belong to this Tangent and participant.");
        if (IsOwner(did)) return true;
        // A durable removal is an explicit override, including for an otherwise open home.
        if (membership?.Role == TangentRole.Removed) return false;
        return membership?.Role == TangentRole.Member || OpenToSignedIn && did is not null && IdentityResolver.IsValidDid(did);
    }

    public static void CheckKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64 || key[0] == '-' || key[^1] == '-'
            || key.Any(c => c is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-'))
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "A Tangent key must contain 1–64 lowercase letters, digits or interior hyphens.");
    }

    private static void CheckCard(string? name, string? description, string? motto, string? accent, string? artwork)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120) throw Invalid("A Tangent name must contain 1–120 characters.");
        if ((description?.Length ?? 0) > 1000) throw Invalid("A Tangent description can contain at most 1000 characters.");
        if ((motto?.Length ?? 0) > 240) throw Invalid("A Tangent motto can contain at most 240 characters.");
        if ((accent?.Length ?? 0) > 64) throw Invalid("A Tangent accent can contain at most 64 characters.");
        if ((artwork?.Length ?? 0) > 2048 || artwork?.Any(char.IsWhiteSpace) == true) throw Invalid("Artwork must be a short local reference or URL without whitespace.");
    }

    private static string Clean(string? value) => value?.Trim() ?? "";
    private static TangentRuleViolation Invalid(string message) => new(TangentDenial.InvalidInput, message);
}
