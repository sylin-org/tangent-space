using System.Security.Cryptography;
using Koan.Data.Core.Model;

namespace TangentSpace.Mcp;

/// <summary>
/// An immutable participation context binding one caller credential and one companion selection to
/// one canonical server origin. Possessing the identifier grants nothing; every use re-validates the
/// credential, participant and origin binding. Contexts created before the companion split carry no
/// binding and are treated as expired, never resurrected.
/// </summary>
public sealed class McpContext : Entity<McpContext>
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    public const string IdPrefix = "ctx_";

    public string CredentialId { get; set; } = "";
    public string ParticipantId { get; set; } = "";
    public string CompanionId { get; set; } = "";
    public string Origin { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public static string NewIdentifier()
        => IdPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static (McpContext Context, bool Created) Issue(string credentialId, string did, string companionId,
        string origin, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(credentialId) || credentialId.Length > 128)
            throw new ArgumentException("A participation credential identifier is required.");
        if (did is null || did.Length == 0) throw new ArgumentException("A verified participant DID is required.");
        if (string.IsNullOrWhiteSpace(companionId) || companionId.Length > 96 || !companionId.StartsWith(McpSelection.IdPrefix, StringComparison.Ordinal))
            throw new ArgumentException("A companion selection identifier is required.");
        if (string.IsNullOrWhiteSpace(origin) || origin.Length > 512)
            throw new ArgumentException("A canonical server origin is required.");
        return (new McpContext
        {
            Id = NewIdentifier(), CredentialId = credentialId, ParticipantId = did,
            CompanionId = companionId, Origin = origin,
            CreatedAt = now, LastUsedAt = now, ExpiresAt = now.Add(Lifetime)
        }, true);
    }

    /// <summary>True only for contexts issued with a companion and origin binding. Unbound legacy
    /// contexts are indistinguishable from expired ones at resolve time.</summary>
    public bool IsBound => CompanionId.Length > 0 && Origin.Length > 0;

    /// <summary>Rolling renewal. The binding itself never changes; only the expiry moves forward on authorized use.</summary>
    public void Touch(DateTimeOffset now)
    {
        if (now > LastUsedAt) LastUsedAt = now;
        ExpiresAt = LastUsedAt.Add(Lifetime);
    }

    public bool IsUsable(DateTimeOffset now) => now < ExpiresAt;
}
