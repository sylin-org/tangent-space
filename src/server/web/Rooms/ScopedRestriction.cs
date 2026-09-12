using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace TangentSpace.Rooms;

/// <summary>
/// One durable, scoped moderation record per participant. Kind None records a lift of the identified
/// local restriction; it never touches an inherited restriction at another scope.
/// </summary>
public sealed class ScopedRestriction : Entity<ScopedRestriction>
{
    public const int MaximumReasonLength = 280;
    public RestrictionScope Scope { get; set; }
    public string ScopeKey { get; set; } = "";
    public string ParticipantId { get; set; } = "";
    public RestrictionKind Kind { get; set; }
    public DateTimeOffset? Until { get; set; }
    public string Reason { get; set; } = "";
    public string ActorParticipantId { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; }

    public static string Key(RestrictionScope scope, string scopeKey, string participantDid)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\n" + scopeKey + "\n" + participantDid)));

    public static ScopedRestriction Impose(RestrictionScope scope, string scopeKey, string targetDid, RestrictionKind kind,
        DateTimeOffset? until, string reason, string actorDid, DateTimeOffset now)
    {
        if (kind != RestrictionKind.Timeout && until is not null)
            throw new RoomRuleViolation(RoomDenial.InvalidInput, "Only a timeout carries an expiry.");
        if (kind == RestrictionKind.Timeout && until is not { } expiry)
            throw new RoomRuleViolation(RoomDenial.InvalidInput, "A timeout needs an explicit future expiry.");
        if (until is { } moment && moment <= now)
            throw new RoomRuleViolation(RoomDenial.InvalidInput, "Choose a timeout expiry in the future.");
        if (kind == RestrictionKind.None && string.IsNullOrWhiteSpace(reason))
            throw new RoomRuleViolation(RoomDenial.InvalidInput, "Give a reason for lifting the restriction.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > MaximumReasonLength)
            throw new RoomRuleViolation(RoomDenial.InvalidInput, "A restriction reason must contain 1–280 characters.");
        return new ScopedRestriction
        {
            Id = Key(scope, scopeKey, targetDid), Scope = scope, ScopeKey = scopeKey, ParticipantId = targetDid,
            Kind = kind, Until = kind == RestrictionKind.Timeout ? until : null, Reason = reason.Trim(),
            ActorParticipantId = actorDid, ChangedAt = now
        };
    }
}
