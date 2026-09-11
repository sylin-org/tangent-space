using TangentSpace.AtProtocol.Verification;

namespace TangentSpace.Mcp.Authentication;

/// <summary>The single seam over guarded issuer DID-key resolution, so verification itself stays real in tests.</summary>
public interface IServiceProofKeySource
{
    Task<ResolvedAuthorKey> Resolve(string issuerDid, CancellationToken ct);
}

/// <summary>Resolves the issuer's current document-controlled #atproto key through the existing pinned, SSRF-safe resolver.</summary>
internal sealed class SpacesServiceProofKeySource(SpacesVerifier verifier) : IServiceProofKeySource
{
    public Task<ResolvedAuthorKey> Resolve(string issuerDid, CancellationToken ct) => verifier.ResolveAuthorKey(issuerDid, ct);
}
