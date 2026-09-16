using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Tangent.Identity;

/// <summary>The inbound MCP proof-exchange endpoint. Accepts only a short-lived AT service-auth JWT
/// in the Authorization header; account credentials never appear in model tool arguments or this body.</summary>
[ApiController, AllowAnonymous, Route(McpAuthenticationConstants.TokenRoute), RequestSizeLimit(8192)]
public sealed class ServiceProofTokenController(ServiceProofExchange exchange, ILogger<ServiceProofTokenController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Exchange([FromBody] ServiceProofExchangeRequest? request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var header = Request.Headers.Authorization;
        if (header.Count != 1 || !header.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { error = "service_proof_required" });
        ServiceProofExchangeResult result;
        try { result = await exchange.Exchange(header.ToString(), request, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogError(error, "MCP proof exchange failed unexpectedly: {FailureType}", error.GetType().Name);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "exchange_failed" });
        }
        return result.Status switch
        {
            ServiceProofExchangeStatus.Issued => Ok(new
            {
                profile = McpAuthenticationConstants.Profile,
                credential = result.Issued!.Credential,
                token = result.Issued.Token
            }),
            ServiceProofExchangeStatus.InvalidProof or ServiceProofExchangeStatus.ReplayedProof
                => Unauthorized(new { error = "invalid_service_proof" }),
            ServiceProofExchangeStatus.UnconfiguredAudience or ServiceProofExchangeStatus.IdentityUnreachable
            or ServiceProofExchangeStatus.SiteUnavailable
                => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "exchange_unavailable" }),
            ServiceProofExchangeStatus.Suspended => StatusCode(StatusCodes.Status403Forbidden, new { error = "participant_suspended" }),
            _ => BadRequest(new { error = result.Reason ?? "invalid_exchange_request" })
        };
    }
}
