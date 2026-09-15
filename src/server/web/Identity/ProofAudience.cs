using Microsoft.Extensions.Options;
using TangentSpace.Site;

namespace TangentSpace.Identity;

/// <summary>The DID that enrollment proofs must name as their audience: the configured
/// Tangent:Enrollment:ProofAudience, or a did:web of the public origin's host and port. The audience
/// of an atproto service-auth token is a bare DID, and Tangent verifies its proofs itself, so the
/// did:web never needs to resolve.</summary>
public sealed class ProofAudience(IOptions<EnrollmentOptions> enrollment, IOptions<SiteOptions> site)
{
    public string? Value => From(enrollment.Value.ProofAudience, site.Value.PublicOrigin);

    public static string? From(string? configured, string? publicOrigin)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        if (!SiteOptions.IsCanonicalOrigin(publicOrigin, out var origin)) return null;
        var uri = new Uri(origin);
        if (uri.HostNameType == UriHostNameType.IPv6) return null;
        return "did:web:" + uri.IdnHost + (uri.IsDefaultPort ? "" : "%3A" + uri.Port);
    }
}
