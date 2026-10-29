using System.Security.Claims;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Auth.Server.Protocol;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Delegates;

/// <summary>The consent page Koan's OAuth server sends a person to, one resource both ways. A visitor is sent to sign
/// in and comes back here. Signed in, the page asks for the code a device or agent shows (Koan's
/// <c>verification_uri</c>; its <c>verification_uri_complete</c> carries the code already), then shows the request
/// waiting under it as Koan's <see cref="ConsentPage.Find"/> answers it: who decides, and that the client acts as them
/// here with everything they can do. A loopback client's request arrives under its <c>rid</c> instead and shows the
/// name it registered and where it returns. Approve and deny post to Koan's request endpoint, which answers the client;
/// the person lands on the done page, which says what they decided as <see cref="ConsentPage.Outcome"/> reads it. No
/// name a client chose is ever shown for a code: only the shared client is allowed the Device flow. Only a person's
/// session decides: a delegate's bearer request is refused here, and Koan decides its own endpoints by the session
/// alone.</summary>
public sealed class ConsentController(KoanWebBaseUrl baseUrl) : ControllerBase
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

    /// <summary>The longest request id the page carries through sign-in, the longest Koan looks up.</summary>
    private const int Carried = 128;

    [HttpGet(Path)]
    public async Task<IActionResult> Get([FromQuery(Name = CodeField)] string? code, [FromQuery(Name = RequestField)] string? rid,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (DelegateIdentity.Delegated(HttpContext)) return NotByDelegate().ToResult();
        if (PersonId(User) is not { } personId)
            return Redirect(Configured() + SignInController.Path + "?return=" + Uri.EscapeDataString(Here(code, rid)));

        var page = await Page(personId, ct);
        if (rid is null && code is null) return Ok(page with { Entry = Entry() });

        var lookup = await ConsentPage.Find(HttpContext, rid ?? code, ct);
        if (lookup.Request is { } pending) return Ok(page with { Decision = Decision(pending) });
        if (code is null || rid is not null)
            return lookup.Throttled
                ? new Problem(StatusCodes.Status429TooManyRequests, "Too many tries",
                    "Too many requests were looked up from here; wait a minute, then start again from the app that asked.").ToResult()
                : new Problem(StatusCodes.Status404NotFound, "Nothing to decide",
                    "This request is unknown or has expired; start again from the app that asked.").ToResult();
        return lookup.Throttled
            ? Again(page, code, StatusCodes.Status429TooManyRequests, "Too many codes were tried from here; wait a minute, then enter it again.")
            : Again(page, code, StatusCodes.Status400BadRequest,
                "No request is waiting for that code: it may be mistyped or expired. Check it, or ask the device or agent for a new one.");
    }

    [HttpGet(DonePath)]
    public async Task<IActionResult> Done(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var page = PersonId(User) is { } personId && !DelegateIdentity.Delegated(HttpContext) ? await Page(personId, ct) : await Page(null, ct);
        var outcome = await ConsentPage.Outcome(HttpContext);
        return Ok(page with { Done = true, Approved = outcome is { } decided ? decided == ConsentOutcome.Approved : null });
    }

    /// <summary>What a delegate's bearer request to the consent page receives: only the person decides.</summary>
    public static Problem NotByDelegate()
        => new(StatusCodes.Status403Forbidden, "Only the person decides",
            "A request made with a bearer token can’t approve or deny an agent; the person signs in here and decides.");

    /// <summary>The request Koan found, its approve and deny as absolute addresses under the base URL. A Device-flow
    /// request shows its code and never the shared client's name; a loopback client's, the name it registered and where
    /// it returns.</summary>
    private ConsentDecision Decision(PendingConsent pending)
    {
        var origin = new Uri(Configured());
        return new ConsentDecision(new Uri(origin, pending.Approve).AbsoluteUri, new Uri(origin, pending.Deny).AbsoluteUri,
            FormToken.For(HttpContext), pending.UserCode, pending.UserCode is null ? pending.ClientName : null, pending.RedirectUri);
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
        => baseUrl.PathBase + Path + (rid is { Length: > 0 and <= Carried } ? "?" + RequestField + "=" + Uri.EscapeDataString(rid)
            : code is { Length: > 0 and <= 32 } ? "?" + CodeField + "=" + Uri.EscapeDataString(code) : "");

    private string Configured() => baseUrl.Configured
        ?? throw new InvalidOperationException("Koan:Web:ApplicationUrl must be set to the base URL the Tangent Space answers at.");

    private static string? PersonId(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
}
