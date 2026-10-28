using System.Net;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Tangent.Spaces;

namespace Tangent.Infrastructure;

/// <summary>Accepts the public HTTPS scheme from one explicitly named reverse proxy before authentication
/// and the browser's same-origin mutation checks run. The proxy must preserve the public Host header.</summary>
public sealed class TrustedProxyPipelineContributor(IConfiguration configuration) : IKoanWebPipelineContributor
{
    public KoanWebPipelineStage Stage => KoanWebPipelineStage.BeforeRouting;

    public void Configure(IApplicationBuilder app)
    {
        var configuredProxy = configuration["Tangent:Proxy:KnownProxy"];
        if (string.IsNullOrWhiteSpace(configuredProxy)) return;
        if (!IPAddress.TryParse(configuredProxy, out var proxy))
            throw new InvalidOperationException("Tangent:Proxy:KnownProxy must be one IP address of the immediate reverse proxy.");

        if (!SpaceOptions.IsCanonicalOrigin(configuration["Tangent:Space:PublicOrigin"], out var origin)
            || new Uri(origin).Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("A trusted proxy requires an HTTPS Tangent:Space:PublicOrigin.");
        if (!string.Equals(configuration["AllowedHosts"], new Uri(origin).Host, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("AllowedHosts must be the public origin's hostname when a trusted proxy is configured.");

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(proxy);
        app.UseForwardedHeaders(options);
    }
}
