using Microsoft.AspNetCore.Authentication;

namespace Tangent.Delegates;

/// <summary>Whether a request is a delegate's. Koan's resource edge (<c>Koan:Web:Auth:ResourceServer:Enabled</c>)
/// authenticates every request: one that carries an <c>Authorization</c> header of the <c>Bearer</c> scheme by its
/// bearer alone, accepted only when Koan's server issued it for this Tangent Space's base URL and read as the person who
/// approved it, under the same <see cref="System.Security.Claims.ClaimTypes.NameIdentifier"/>, their DID, as their
/// session; any other, including one whose header has another scheme, by the session cookie. A tool's call is a request
/// with no credential at all, whose principal Koan's edge at <c>/mcp</c> placed from a bearer it accepted. So the
/// participant, <c>can</c> and every gate answer for a delegate exactly as for its person.</summary>
public static class DelegateIdentity
{
    /// <summary>The claim a bearer names the resource it was issued for by, which no session holds.</summary>
    private const string Audience = "aud";

    /// <summary>Whether this request is a delegate's: Koan authenticated it by a bearer token, on this request or at the
    /// edge a tool's call came through, and a bearer carries no ambient credential.</summary>
    public static bool Delegated(HttpContext http)
        => http.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult is { Succeeded: true, Ticket.Principal: var principal } && principal.HasClaim(claim => claim.Type == Audience);
}
