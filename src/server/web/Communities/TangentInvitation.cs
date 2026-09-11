using Koan.Data.Core.Model;

namespace TangentSpace.Communities;

/// <summary>
/// A DID- and Tangent-bound invitation. Issuing never joins anyone and never delivers an external message;
/// redemption happens only through Join with the bound recipient's own credential.
/// </summary>
public sealed class TangentInvitation : Entity<TangentInvitation>
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(14);

    public string TangentKey { get; set; } = "";
    public string RecipientDid { get; set; } = "";
    public TangentRole GrantedRole { get; set; }
    public string IssuerDid { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string RevokedByDid { get; set; } = "";
    public DateTimeOffset? RedeemedAt { get; set; }
    public string RedeemedByDid { get; set; } = "";

    public static bool IsValidGrant(TangentRole role) => role is TangentRole.Member or TangentRole.Admin or TangentRole.Reader;

    public static TangentInvitation Issue(TangentCommunity tangent, string recipientDid, TangentRole grantedRole,
        string issuerDid, DateTimeOffset now)
    {
        if (!IsValidGrant(grantedRole) || !Enum.IsDefined(grantedRole))
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "An invitation grants member, reader, or admin.");
        return new TangentInvitation
        {
            Id = Guid.CreateVersion7().ToString("N"), TangentKey = tangent.Id, RecipientDid = recipientDid,
            GrantedRole = grantedRole, IssuerDid = issuerDid, IssuedAt = now, ExpiresAt = now.Add(DefaultLifetime)
        };
    }

    public bool Usable(DateTimeOffset now) => RevokedAt is null && RedeemedAt is null && ExpiresAt > now;

    public void Revoke(string actorDid, DateTimeOffset now)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        RevokedByDid = actorDid;
    }

    public void Redeem(string actorDid, DateTimeOffset now)
    {
        if (RevokedAt is not null) throw new TangentRuleViolation(TangentDenial.Forbidden, "The invitation was revoked.");
        if (RedeemedAt is not null) throw new TangentRuleViolation(TangentDenial.InvalidInput, "The invitation was already redeemed.");
        if (ExpiresAt <= now) throw new TangentRuleViolation(TangentDenial.Forbidden, "The invitation expired.");
        RedeemedAt = now;
        RedeemedByDid = actorDid;
    }
}
