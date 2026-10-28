using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tangent.Spaces;
using Tangent.Activity;

namespace Tangent.Identity;

/// <summary>Public discovery for service-proof enrollment: the configured canonical public origin and the
/// proof audience and method the connector must request.</summary>
[ApiController, AllowAnonymous, Route(ServiceAuthenticationConstants.DiscoveryRoute)]
public sealed class TangentAuthDiscoveryController(ProofAudience proofAudience, IOptions<SpaceOptions> space) : ControllerBase
{
    [HttpGet]
    public IActionResult Discover()
    {
        Response.Headers.CacheControl = "no-store";
        if (proofAudience.Value is not { } audience)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "exchange_unconfigured" });
        // The canonical origin comes from configuration (Tangent:Space:PublicOrigin), never from the request Host.
        if (!SpaceOptions.IsCanonicalOrigin(space.Value.PublicOrigin, out var origin))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "public_origin_unconfigured" });
        return Ok(new
        {
            canonicalOrigin = origin,
            serviceProof = new
            {
                method = ServiceAuthenticationConstants.ExchangeMethod,
                purpose = ServiceAuthenticationConstants.ExchangeMethod,
                audience,
                algorithms = new[] { "ES256", "ES256K" },
                transport = "authorization_header",
                endpoint = origin + "/" + ServiceAuthenticationConstants.TokenRoute
            },
            credentials = new
            {
                type = "tangent_participant_token",
                transport = "authorization_header",
                defaultLifetimeDays = 1,
                grants = new[] { ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post, ParticipationGrants.Manage },
                manageGrantPolicy = "explicitly requested transport ceiling; no role is assigned, and every privileged mutation verifies current domain authority"
            }
        });
    }
}
