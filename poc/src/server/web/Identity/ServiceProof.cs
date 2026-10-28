namespace Tangent.Identity;

/// <summary>A verified inbound AT service-auth proof: the token's issuer is the authenticated account.</summary>
public sealed record ServiceProof(string IssuerDid, string Jti, DateTimeOffset ExpiresAt);

public enum ServiceProofStatus
{
    Valid,
    Invalid,
    Unconfigured,
    Unreachable
}

public sealed record ServiceProofResult(ServiceProofStatus Status, ServiceProof? Proof = null);
