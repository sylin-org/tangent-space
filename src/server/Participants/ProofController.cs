using System.Text.Json;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Server.Protocol;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>Where an agent with an atproto identity of its own signs in with no browser. It asks its account's server for a
/// service-auth token addressed to this place and sends it here as <c>Authorization: Bearer</c>. Koan verifies the token and
/// ends the sign-in as a browser's ends: the same lifecycle, so the same admission, roles, first run and ban, and the same
/// cookie session, ended the same ways, and its answer holds a bearer of the place's for the same participant. The token must be addressed to the address the place has configured or accepted,
/// never one a request says, so a token addressed to another place signs no one in here, and a place with neither takes no
/// token and says why. A place nobody has set up takes no token either, or the first agent to connect would own it, unless
/// the configuration names the account that may set it up, as it may name an agent. Its only credential is the header, which the resource edge is not to read as a bearer of this
/// application's, and it reads no body.</summary>
[AnswersBeforeFirstRun]
[AnswersEveryVisitor]
[ResourceServerExempt]
public sealed class ProofController(AtprotoServiceAuth proofs, KoanWebBaseUrl baseUrl, IOptions<TangentSpaceOptions> options, ILogger<ProofController> logger) : ControllerBase
{
    /// <summary>What a place with no address of its own says at this door.</summary>
    public const string NoAddress = "This Tangent Space has no address of its own yet, and a token is addressed to one, so it takes no token. Signing in through atproto gives it one, or its operator configures one.";

    /// <summary>What a place nobody has set up says at this door, when the configuration names no account that may.</summary>
    public const string NotSetUp = "This Tangent Space has not been set up yet, so it takes no token, or the first agent to come would own it. Someone sets it up by signing in here in a browser, or its operator names the account that may.";

    /// <summary>What a subject hears who proved itself and may not act here.</summary>
    public const string MayNotAct = "This account proved itself and may not act here now, so this place gives it no token.";

    private const string BearerPrefix = "Bearer ";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>What the door takes, with every address and word a client needs to prove itself here and nowhere else.</summary>
    [HttpGet(ProofDoor.StatementPath)]
    [HttpHead(ProofDoor.StatementPath)]
    public async Task<IActionResult> Statement(CancellationToken ct)
    {
        if (baseUrl.Current is not { } place) return Unaddressed();
        if (await WaitsForAPerson(ct)) return NotYetSetUp();
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

    /// <summary>Takes a service-auth token and signs its issuer in, with the session's cookie, and answers with a bearer for
    /// the same participant (Koan's <see cref="AccessToken"/>), which the place's pages and its tools take alike. A
    /// subject who may not act answers <c>403</c>. The token sent is never echoed.</summary>
    [HttpPost(ProofDoor.ExchangePath)]
    public async Task<IActionResult> Exchange(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (baseUrl.Current is not { } place) return Unaddressed();
        if (await WaitsForAPerson(ct)) return NotYetSetUp();
        var header = Request.Headers.Authorization;
        if (header.Count != 1 || !header[0]!.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return Answer(StatusCodes.Status401Unauthorized, new { error = "service_proof_required" });

        var proof = await proofs.Verify(header[0]![BearerPrefix.Length..], ProofDoor.Audience(place), ProofDoor.Method, ct);
        if (proof.Outcome == AtprotoServiceAuthOutcome.IssuerUnreachable)
            return Answer(StatusCodes.Status503ServiceUnavailable, new { error = "exchange_unavailable" });
        if (!proof.Verified) return Answer(StatusCodes.Status401Unauthorized, new { error = "invalid_service_proof" });

        var signedIn = await proofs.SignIn(HttpContext, proof);
        logger.LogInformation("{Participant} proved itself and signed in", proof.Did);
        if (await AccessToken.For(HttpContext, signedIn, ct) is not { } bearer)
            return Answer(StatusCodes.Status403Forbidden, new { error = "not_open", message = MayNotAct });
        return Answer(StatusCodes.Status200OK, new { access_token = bearer.Value, token_type = "Bearer", expires_in = (int)bearer.Lifetime.TotalSeconds });
    }

    /// <summary>Whether nobody has set the place up and the configuration names no account that may. One bounded read of the
    /// row by its key.</summary>
    private async Task<bool> WaitsForAPerson(CancellationToken ct)
        => options.Value.NamedOperator() is null && await TangentSpace.Get(TangentSpace.SingleRow, ct) is null;

    private ContentResult NotYetSetUp() => Answer(StatusCodes.Status503ServiceUnavailable, new { error = "exchange_not_set_up", message = NotSetUp });

    private ContentResult Unaddressed() => Answer(StatusCodes.Status503ServiceUnavailable, new { error = "exchange_unconfigured", message = NoAddress });

    private static ContentResult Answer(int status, object body)
        => new() { StatusCode = status, ContentType = "application/json", Content = JsonSerializer.Serialize(body, Json) };
}
