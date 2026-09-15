using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TangentSpace.AtProtocol;
using TangentSpace.Participation;
using TangentSpace.Site;

namespace TangentSpace.Mcp.Authentication;

/// <summary>Public discovery for service-proof enrollment: the configured canonical public origin and the
/// proof audience and method the connector must request.</summary>
[ApiController, AllowAnonymous, Route(McpAuthenticationConstants.DiscoveryRoute)]
public sealed class TangentMcpDiscoveryController(IOptions<SpacesOptions> spaces, IOptions<SiteOptions> site) : ControllerBase
{
    [HttpGet]
    public IActionResult Discover()
    {
        Response.Headers.CacheControl = "no-store";
        var audience = spaces.Value.ManagingApp;
        if (string.IsNullOrWhiteSpace(audience))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "exchange_unconfigured" });
        // The canonical origin comes from configuration (Tangent:Site:PublicOrigin), never from the request Host.
        if (!SiteOptions.IsCanonicalOrigin(site.Value.PublicOrigin, out var origin))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "public_origin_unconfigured" });
        return Ok(new
        {
            protocolVersion = McpAuthenticationConstants.ProtocolVersion,
            authenticationProfile = McpAuthenticationConstants.Profile,
            serviceProof = new
            {
                method = McpAuthenticationConstants.ExchangeMethod,
                audience,
                algorithms = new[] { "ES256", "ES256K" },
                transport = "authorization_header",
                endpoint = origin + "/" + McpAuthenticationConstants.TokenRoute
            },
            credentials = new
            {
                type = "tangent_participant_token",
                transport = "authorization_header",
                defaultLifetimeDays = 1,
                grants = new[] { ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post, ParticipationGrants.Manage },
                manageGrantPolicy = "explicit request only; every domain mutation independently verifies current authority, so the grant never appoints anyone"
            },
            sourceWriteConsent = new
            {
                includedInProof = false,
                description = "A verified service-auth proof authenticates the account DID only; source Spaces writes still require the account's separate source-write consent.",
                obtainConsent = "/api/connections/rooms"
            }
        });
    }
}
