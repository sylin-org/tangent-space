using System.Security.Cryptography;
using Koan.Data.Core.Model;

namespace TangentSpace.Mcp;

/// <summary>
/// A durable companion selection: one caller credential has chosen one verified companion DID.
/// Possessing the identifier grants nothing; every use re-validates the credential and participant.
/// A selection never carries a server binding; Arrive turns it into a bound context.
/// </summary>
public sealed class McpSelection : Entity<McpSelection>
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    public const string IdPrefix = "cmp_";

    public string CredentialId { get; set; } = "";
    public string ParticipantDid { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    public static string NewIdentifier()
        => IdPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static (McpSelection Selection, bool Created) Issue(string credentialId, string did, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(credentialId) || credentialId.Length > 128)
            throw new ArgumentException("A participation credential identifier is required.");
        if (did is null || did.Length == 0) throw new ArgumentException("A verified participant DID is required.");
        return (new McpSelection
        {
            Id = NewIdentifier(), CredentialId = credentialId, ParticipantDid = did,
            CreatedAt = now, LastUsedAt = now, ExpiresAt = now.Add(Lifetime)
        }, true);
    }

    /// <summary>Rolling renewal. The binding itself never changes; only the expiry moves forward on authorized use.</summary>
    public void Touch(DateTimeOffset now)
    {
        if (now > LastUsedAt) LastUsedAt = now;
        ExpiresAt = LastUsedAt.Add(Lifetime);
    }

    public bool IsUsable(DateTimeOffset now) => now < ExpiresAt;
}
