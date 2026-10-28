using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tangent.Spaces;

namespace Tangent.Identity;

/// <summary>Publishes the service DID owned by the configured public HTTPS origin.</summary>
[ApiController, AllowAnonymous, Route(ServiceAuthenticationConstants.DidDocumentRoute)]
public sealed class ServiceDidDocumentController(ProofAudience proofAudience, IOptions<SpaceOptions> space) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        Response.Headers.CacheControl = "no-store";
        if (!SpaceOptions.IsCanonicalOrigin(space.Value.PublicOrigin, out var origin)) return NotFound();
        var uri = new Uri(origin);
        if (uri.Scheme != Uri.UriSchemeHttps || uri.IsLoopback) return NotFound();

        var did = ProofAudience.From(null, origin);
        if (did is null || proofAudience.Value != did + "#tangent") return NotFound();

        return Ok(new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/did/v1",
            ["id"] = did,
            ["service"] = new[]
            {
                new { id = did + "#tangent", type = "TangentSpace", serviceEndpoint = origin }
            }
        });
    }
}
