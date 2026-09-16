using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace Tangent.Identity;

/// <summary>Revocable local participation authority. Id is the token hash; the token itself is never persisted.</summary>
public sealed class ParticipantCredential : Entity<ParticipantCredential>
{
    public string ParticipantId { get; set; } = "";
    public string Name { get; set; } = "";
    public string[] Grants { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    internal static (ParticipantCredential Credential, string Token) Issue(string participantId, string name, int lifetimeDays,
        string[] grants, DateTimeOffset now, bool managementPermitted = false)
    {
        if (!Participant.IsValidId(participantId)) throw new ArgumentException("A verified participant is required.");
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80 || name.Any(char.IsControl)) throw new ArgumentException("Name must contain 1–80 printable characters.");
        if (lifetimeDays is < 1 or > 30) throw new ArgumentException("Credential lifetime must be 1–30 days.");
        if (grants is null || grants.Length is 0 or > 4 || grants.Any(grant => !ParticipationGrants.IsKnown(grant))) throw new ArgumentException("Only welcome, read, post, and manage grants are supported.");
        if (grants.Contains(ParticipationGrants.Manage) && !managementPermitted) throw new ArgumentException("The manage grant requires current host management authorization.");
        var token = ParticipationConstants.TokenPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (new ParticipantCredential
        {
            Id = Hash(token)!, ParticipantId = participantId, Name = name.Trim(), Grants = grants.Distinct(StringComparer.Ordinal).ToArray(),
            CreatedAt = now, ExpiresAt = now.AddDays(lifetimeDays)
        }, token);
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now >= CreatedAt && now < ExpiresAt;

    public void Revoke(string participantId, DateTimeOffset now)
    {
        if (ParticipantId != participantId) throw new UnauthorizedAccessException("This credential belongs to another participant.");
        RevokedAt ??= now;
    }

    internal static string? Hash(string token)
    {
        if (token.Length != 46 || !token.StartsWith(ParticipationConstants.TokenPrefix, StringComparison.Ordinal)
            || token.AsSpan(3).ContainsAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_")) return null;
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
    }
}
