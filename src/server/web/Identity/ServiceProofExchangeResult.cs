
namespace Tangent.Identity;

public enum ServiceProofExchangeStatus
{
    Issued,
    BadRequest,
    InvalidProof,
    ReplayedProof,
    UnconfiguredAudience,
    IdentityUnreachable,
    SiteUnavailable,
    Suspended
}

public sealed record ServiceProofExchangeResult(ServiceProofExchangeStatus Status, CredentialIssued? Issued = null, string? Reason = null);
