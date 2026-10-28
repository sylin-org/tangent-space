using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Koan.Web.Auth.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tangent.Identity;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class TrustedProxyPipelineTests
{
    [Fact]
    public async Task Forwarded_https_allows_cookie_write_only_from_configured_proxy()
    {
        const string publicOrigin = "https://tangent.example";
        await using (var trusted = await ExperienceWebApp.StartAsync(publicOrigin, IPAddress.Loopback.ToString()))
        {
            using var accepted = await PostThroughProxy(trusted, publicOrigin, "trusted-proxy-post");
            accepted.EnsureSuccessStatusCode();
        }

        await using (var untrusted = await ExperienceWebApp.StartAsync(publicOrigin, "127.0.0.2"))
        {
            using var rejected = await PostThroughProxy(untrusted, publicOrigin, "untrusted-proxy-post");
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        }
    }

    private static async Task<HttpResponseMessage> PostThroughProxy(ExperienceWebApp app, string publicOrigin, string requestId)
    {
        var scheme = AuthenticationExtensions.CookieScheme;
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ParticipationConstants.ParticipantClaim, app.AgentParticipantId)], "browser"));
        var cookie = options.Cookie.Name + "=" + options.TicketDataFormat.Protect(
            new AuthenticationTicket(principal, scheme));

        using var browser = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(app.Origin)
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/experience/topics/proj-z/posts")
        {
            Content = JsonContent.Create(new { requestId, text = "A proxied browser contribution." })
        };
        request.Headers.Host = new Uri(publicOrigin).Host;
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("Origin", publicOrigin);
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");
        return await browser.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
