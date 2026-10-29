using System.Security.Claims;
using Koan.Security.Trust.Inbound;
using Koan.Web.Auth.Extensions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Tangent.Delegates;

/// <summary>Who a request is, once, for every page: a request that carries a bearer token (an <c>Authorization</c>
/// header of the <c>Bearer</c> scheme) is authenticated by Koan's bearer scheme alone and never by the session cookie;
/// any other, including one whose header has another scheme, such as a proxy's basic credentials, by the session
/// cookie. A
/// bearer token Koan's server issued is the person who approved it, under the same
/// <see cref="ClaimTypes.NameIdentifier"/>, their DID, as their session, so the participant, <c>can</c> and every gate
/// answer for their delegate exactly as for them; and only when it was issued for this Tangent Space's base URL, since
/// a token issued for another resource is not this one's to accept.</summary>
public static class DelegateIdentity
{
    /// <summary>The scheme every request is authenticated by: it forwards to the bearer or to the cookie.</summary>
    public const string Scheme = "Tangent.SessionOrBearer";

    /// <summary>The scheme that authenticates this request: Koan's bearer when it carries a bearer token, whatever
    /// cookie it also carries; the session cookie otherwise.</summary>
    public static string Select(HttpContext http)
        => CarriesBearer(http) ? KoanBearerDefaults.AuthenticationScheme : AuthenticationExtensions.CookieScheme;

    /// <summary>Whether this request carries a bearer token: an <c>Authorization</c> header whose scheme is
    /// <c>Bearer</c>, in any letter case, valid or not.</summary>
    public static bool CarriesBearer(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        return header.Length > 7 && header.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase) && header[6] == ' ';
    }

    /// <summary>Accepts a token Koan's bearer scheme has verified when its audience is this Tangent Space's base URL,
    /// and makes its subject the person's identifier; refuses it otherwise, so the request reads as carrying a
    /// rejected token.</summary>
    public static Task Admit(TokenValidatedContext context, KoanWebBaseUrl baseUrl)
    {
        var principal = context.Principal;
        if (principal?.Identity is not ClaimsIdentity identity || baseUrl.Configured is not { } resource
            || !principal.FindAll("aud").Any(audience => audience.Value == resource || audience.Value == resource + "/"))
        {
            context.Fail("The token was not issued for this Tangent Space.");
            return Task.CompletedTask;
        }

        if (principal.FindFirst("sub")?.Value is not { Length: > 0 } subject)
        {
            context.Fail("The token names no person.");
            return Task.CompletedTask;
        }

        if (identity.FindFirst(ClaimTypes.NameIdentifier) is null) identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, subject));
        return Task.CompletedTask;
    }

    /// <summary>Whether this request is a delegate's: authenticated by a bearer token, which carries no ambient
    /// credential.</summary>
    public static bool Delegated(HttpContext http)
        => http.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult is { Succeeded: true, Ticket.AuthenticationScheme: KoanBearerDefaults.AuthenticationScheme };

    /// <summary>Whether this request carried a bearer token that authenticated nobody: expired, tampered, signed by
    /// another issuer or issued for another resource.</summary>
    public static bool Rejected(HttpContext http) => CarriesBearer(http) && http.User.Identity?.IsAuthenticated != true;
}
