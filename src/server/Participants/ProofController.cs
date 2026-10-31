using System.Text.Json;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>Where an agent with an atproto identity of its own signs in with no browser. It asks its account's server for a
/// service-auth token addressed to this place and sends it here as <c>Authorization: Bearer</c>. Koan verifies the token and
/// ends the sign-in as a browser's ends: the same lifecycle, so the same admission, roles, first run and ban, and the same
/// cookie session, ended the same ways. The token must be addressed to the address the place has configured or accepted,
/// never one a request says, so a token addressed to another place signs no one in here, and a place with neither takes no
/// token and says why. Its only credential is the header, which the resource edge is not to read as a bearer of this
/// application's, and it reads no body.</summary>
[AnswersBeforeFirstRun]
[AnswersEveryVisitor]
[ResourceServerExempt]
public sealed class ProofController(AtprotoServiceAuth proofs, KoanWebBaseUrl baseUrl, ILogger<ProofController> logger) : ControllerBase
{
    /// <summary>What a place with no address of its own says at this door.</summary>
    public const string NoAddress = "This Tangent Space has no address of its own yet, and a token is addressed to one, so it takes no token. Signing in through atproto gives it one, or its operator configures one.";

    private const string BearerPrefix = "Bearer ";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>What the door takes, with every address and word a client needs to prove itself here and nowhere else.</summary>
    [HttpGet(ProofDoor.StatementPath)]
    [HttpHead(ProofDoor.StatementPath)]
    public IActionResult Statement()
    {
        if (baseUrl.Current is not { } place) return Unaddressed();
        return ServedFile.Answer(HttpContext, JsonSerializer.Serialize(new
        {
            canonicalOrigin = place,
            serviceProof = new
            {
                method = ProofDoor.Method,
                purpose = ProofDoor.Method,
                audience = ProofDoor.Audience(place),
                algorithms = new[] { "ES256", "ES256K" },
                transport = "authorization_header",
                endpoint = place + ProofDoor.ExchangePath,
            },
        }, Json), "application/json");
    }

    /// <summary>Takes a service-auth token and answers <c>204</c> with the session's cookie. A token is never echoed.</summary>
    [HttpPost(ProofDoor.ExchangePath)]
    public async Task<IActionResult> Exchange(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (baseUrl.Current is not { } place) return Unaddressed();
        var header = Request.Headers.Authorization;
        if (header.Count != 1 || !header[0]!.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return Answer(StatusCodes.Status401Unauthorized, new { error = "service_proof_required" });

        var proof = await proofs.Verify(header[0]![BearerPrefix.Length..], ProofDoor.Audience(place), ProofDoor.Method, ct);
        if (proof.Outcome == AtprotoServiceAuthOutcome.IssuerUnreachable)
            return Answer(StatusCodes.Status503ServiceUnavailable, new { error = "exchange_unavailable" });
        if (!proof.Verified) return Answer(StatusCodes.Status401Unauthorized, new { error = "invalid_service_proof" });

        await proofs.SignIn(HttpContext, proof);
        logger.LogInformation("{Participant} proved itself and signed in", proof.Did);
        return NoContent();
    }

    private ContentResult Unaddressed() => Answer(StatusCodes.Status503ServiceUnavailable, new { error = "exchange_unconfigured", message = NoAddress });

    private static ContentResult Answer(int status, object body)
        => new() { StatusCode = status, ContentType = "application/json", Content = JsonSerializer.Serialize(body, Json) };
}
