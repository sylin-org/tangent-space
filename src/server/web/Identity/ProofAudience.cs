using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TangentSpace.Site;

namespace TangentSpace.Identity;

/// <summary>The DID that enrollment proofs must name as their audience: the configured
/// Tangent:Enrollment:ProofAudience, or a did:web derived from the public origin within atproto's
/// did:web profile, which admits a hostname and allows a port only for localhost. A loopback origin
/// derives did:web:localhost with its port; a hostname on its default port derives did:web of that
/// host; an IP literal or any other port derives nothing and needs a configured audience. Tangent
/// verifies its proofs itself, so the did:web never needs to resolve.</summary>
public sealed partial class ProofAudience(IOptions<EnrollmentOptions> enrollment, IOptions<SiteOptions> site)
{
    public string? Value => From(enrollment.Value.ProofAudience, site.Value.PublicOrigin);

    public static string? From(string? configured, string? publicOrigin)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim();
        if (!SiteOptions.IsCanonicalOrigin(publicOrigin, out var origin)) return null;
        var uri = new Uri(origin);
        if (uri.IsLoopback) return "did:web:localhost" + (uri.IsDefaultPort ? "" : "%3A" + uri.Port);
        if (uri.HostNameType != UriHostNameType.Dns || !uri.IsDefaultPort) return null;
        return "did:web:" + uri.IdnHost;
    }

    /// <summary>Whether an atproto account server accepts the DID as a service-auth audience:
    /// did:plc, or did:web of a hostname, with a port only for localhost.</summary>
    public static bool IsAtprotoAudience(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        if (value.StartsWith("did:plc:", StringComparison.Ordinal)) return PlcDid().IsMatch(value);
        if (!value.StartsWith("did:web:", StringComparison.Ordinal)) return false;
        var host = value["did:web:".Length..];
        var port = host.IndexOf("%3A", StringComparison.OrdinalIgnoreCase);
        if (port >= 0)
            return host[..port] == "localhost" && ushort.TryParse(host[(port + 3)..], out var number) && number > 0;
        return host == "localhost" || Uri.CheckHostName(host) == UriHostNameType.Dns && host.Contains('.');
    }

    [GeneratedRegex("^did:plc:[a-z2-7]{24}$", RegexOptions.CultureInvariant)]
    private static partial Regex PlcDid();
}
