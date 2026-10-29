using System.Net;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Tangent.Space;

/// <summary>Accepts the public scheme from the one configured reverse proxy, before authentication and
/// the cookie checks read it. The proxy must preserve the public Host header. Configuration validation
/// has already required an https base URL and a matching AllowedHosts.</summary>
public sealed class TrustedProxyPipelineContributor(IOptions<TangentSpaceOptions> options) : IKoanWebPipelineContributor
{
    public KoanWebPipelineStage Stage => KoanWebPipelineStage.BeforeRouting;

    public int Order => -20;

    public void Configure(IApplicationBuilder app)
    {
        if (options.Value.TrustedProxy is not { } trustedProxy) return;
        var forwarded = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
        forwarded.KnownProxies.Add(IPAddress.Parse(trustedProxy));
        app.UseForwardedHeaders(forwarded);
    }
}
