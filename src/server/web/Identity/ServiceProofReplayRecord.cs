using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace Tangent.Identity;

/// <summary>Durable single-use consumption of one service-auth proof, keyed by issuer and jti.
/// Id is the SHA-256 of the issuer/jti pair; the record outlives the proof by a small skew.</summary>
public sealed class ServiceProofReplayRecord : Entity<ServiceProofReplayRecord>
{
    public string IssuerDid { get; set; } = "";
    public string Jti { get; set; } = "";
    public string CredentialId { get; set; } = "";
    public DateTimeOffset ConsumedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    internal static string Key(string issuerDid, string jti)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(issuerDid + "\n" + jti)));
}
