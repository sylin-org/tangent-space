using Koan.Web.Auth.Connector.Atproto.Protocol;
using Tangent.Activity;

namespace Tangent.Identity;

/// <summary>The single seam over guarded issuer DID-key resolution, so verification itself stays real in tests.</summary>
public interface IServiceProofKeySource
{
    Task<DidSigningKey> Resolve(string issuerDid, CancellationToken ct);
}

/// <summary>Resolves the issuer's current document-controlled #atproto key through the pinned, SSRF-safe DID resolver.</summary>
internal sealed class DidDocumentKeySource(AtprotoSessions sessions) : IServiceProofKeySource
{
    public async Task<DidSigningKey> Resolve(string issuerDid, CancellationToken ct)
        => DidSigningKey.FromDidDocument(await sessions.ResolveDid(issuerDid, ct), issuerDid);
}
