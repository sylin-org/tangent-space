using System.Security.Claims;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Server.Options;
using Koan.Web.Auth.Server.Protocol;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Delegates;

/// <summary>The consent page Koan's OAuth server sends a person to, one resource both ways. A visitor is sent to sign
/// in and comes back here. Signed in, the page asks for the code a device or agent shows (Koan's
/// <c>verification_uri</c>; its <c>verification_uri_complete</c> carries the code already), then shows the request
/// waiting under it: who decides, and that the client acts as them here with everything they can do. A loopback
/// client's request arrives under its <c>rid</c> instead and shows the name it registered and where it returns.
/// Approve and deny post to Koan's request endpoint, which answers the client; the person lands on the done page.
/// Looking a code up is rate-limited per address as Koan's own verification is. Only a person's session decides: a
/// delegate's bearer request is refused.</summary>
public sealed class ConsentController(KoanWebBaseUrl baseUrl, IOptions<AuthServerOptions> server, FixedWindowRateLimiter limiter,
    TimeProvider time) : ControllerBase
{
    /// <summary>The page's path under the base URL, which Koan's server is told as its consent path.</summary>
    public const string Path = "/consent";

    /// <summary>Where Koan's server sends the person once they have decided a Device-flow request.</summary>
    public const string DonePath = Path + "/done";

    /// <summary>The query field that carries a device's code, as Koan's <c>verification_uri_complete</c> names it
    /// (RFC 8628).</summary>
    public const string CodeField = "user_code";

    /// <summary>The query field that carries a loopback client's request.</summary>
    public const string RequestField = "rid";

    /// <summary>Koan's request endpoint under the base URL; a request's approve and deny follow its id.</summary>
    public const string RequestEndpoint = "/oauth/request/";

    /// <summary>The longest code or request id the page looks up, as Koan's endpoint accepts.</summary>
    public const int IdLimit = 128;

    [HttpGet(Path)]
    public async Task<IActionResult> Get([FromQuery(Name = CodeField)] string? code, [FromQuery(Name = RequestField)] string? rid,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (DelegateIdentity.Delegated(HttpContext)) return NotByDelegate().ToResult();
        if (PersonId(User) is not { } personId)
            return Redirect(Configured() + SignInController.Path + "?return=" + Uri.EscapeDataString(Here(code, rid)));

        var page = await Page(personId, ct);
        if (rid is not null) return await ForRequest(page, rid, ct);
        if (code is null) return Ok(page with { Entry = Entry() });

        var now = time.GetUtcNow();
        var address = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!limiter.TryAcquire("consent:" + address, server.Value.UserCodeVerificationRateLimitPerMinute, TimeSpan.FromMinutes(1), now))
            return Again(page, code, StatusCodes.Status429TooManyRequests, "Too many codes were tried from here; wait a minute, then enter it again.");

        var normalized = Normalize(code);
        var device = normalized.Length is > 0 and <= IdLimit
            ? (await DeviceCode.Query(row => row.UserCode == normalized, ct)).FirstOrDefault(row => row.IsPending && !row.IsExpired(now))
            : null;
        if (device is null)
            return Again(page, code, StatusCodes.Status400BadRequest,
                "No request is waiting for that code: it may be mistyped or expired. Check it, or ask the device or agent for a new one.");

        var client = device.ClientId == DelegateClient.Id ? null : (await OAuthClient.Get(device.ClientId, ct))?.ClientName is { Length: > 0 } name ? name : device.ClientId;
        return Ok(page with { Decision = Decision(normalized, Format(normalized), client, null) });
    }

    [HttpGet(DonePath)]
    public async Task<IActionResult> Done(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var page = PersonId(User) is { } personId && !DelegateIdentity.Delegated(HttpContext) ? await Page(personId, ct) : await Page(null, ct);
        return Ok(page with { Done = true });
    }

    /// <summary>What a delegate's bearer request to the consent ceremony receives: only the person decides.</summary>
    public static Problem NotByDelegate()
        => new(StatusCodes.Status403Forbidden, "Only the person decides",
            "A request made with a bearer token can’t approve or deny an agent; the person signs in here and decides.");

    /// <summary>A loopback client's request, waiting under its id: the name it registered and where it returns.</summary>
    private async Task<IActionResult> ForRequest(Consent page, string rid, CancellationToken ct)
    {
        var request = rid.Length is > 0 and <= IdLimit ? await ConsentRequest.Get(rid, ct) : null;
        if (request is null || !request.IsPending || request.IsExpired(time.GetUtcNow()))
            return new Problem(StatusCodes.Status404NotFound, "Nothing to decide",
                "This request is unknown or has expired; start again from the app that asked.").ToResult();
        var client = (await OAuthClient.Get(request.ClientId, ct))?.ClientName is { Length: > 0 } name ? name : request.ClientId;
        return Ok(page with { Decision = Decision(rid, null, client, request.RedirectUri) });
    }

    private ConsentDecision Decision(string id, string? code, string? client, string? redirect)
    {
        var endpoint = Configured() + RequestEndpoint + Uri.EscapeDataString(id);
        return new ConsentDecision(endpoint + "/approve", endpoint + "/deny", FormToken.For(HttpContext), code, client, redirect);
    }

    private ConsentEntry Entry(string? code = null, string? failure = null) => new(Configured() + Path, CodeField, code, failure);

    /// <summary>The code form again, keeping what was typed, with why nothing was found.</summary>
    private ObjectResult Again(Consent page, string code, int status, string failure)
    {
        var kept = code.Length <= 32 && code.All(c => c is > ' ' and <= '~') ? code : null;
        return new ObjectResult(page with { Entry = Entry(kept, failure) }) { StatusCode = status };
    }

    /// <summary>The page in any state: the place, its door and who decides.</summary>
    private async Task<Consent> Page(string? personId, CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        return new Consent(space.Name, Configured() + "/", personId is null ? null : await Reader(personId, ct));
    }

    /// <summary>The signed-in person as the account menu shows them, signing out back to the consent page.</summary>
    private async Task<Who> Reader(string personId, CancellationToken ct)
    {
        var participant = await Participant.Get(personId, ct);
        var handle = participant?.Handle is { Length: > 0 } stored ? stored
            : User.FindFirst(Koan.Web.Auth.Connector.Atproto.AtprotoClaimTypes.Handle)?.Value is { Length: > 0 } claimed ? claimed : personId;
        var name = participant?.Profile.DisplayName is { Length: > 0 } displayName ? displayName : handle;
        var role = participant is null ? null : Roles.Word((await Capabilities.Load(personId, null, null, ct)).RoleIds);
        var signOut = Configured() + AuthConstants.Routes.Logout + "?return=" + Uri.EscapeDataString(baseUrl.PathBase + Path);
        return new Who(personId, name, handle, role, participant?.Profile.Avatar, signOut);
    }

    /// <summary>This page's address under the base URL with what it was asked for, for sign-in to come back to.</summary>
    private string Here(string? code, string? rid)
        => baseUrl.PathBase + Path + (rid is { Length: > 0 and <= IdLimit } ? "?" + RequestField + "=" + Uri.EscapeDataString(rid)
            : code is { Length: > 0 and <= 32 } ? "?" + CodeField + "=" + Uri.EscapeDataString(code) : "");

    /// <summary>A typed code in the form Koan stores it: its letters and digits, upper case.</summary>
    private static string Normalize(string code) => new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>A stored code as a device shows it: two groups of four.</summary>
    private static string Format(string code) => code.Length == 8 ? code[..4] + "-" + code[4..] : code;

    private string Configured() => baseUrl.Configured
        ?? throw new InvalidOperationException("Koan:Web:ApplicationUrl must be set to the base URL the Tangent Space answers at.");

    private static string? PersonId(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
}
