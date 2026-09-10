using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TangentSpace.Rooms.Web;

/// <summary>Cookie-only administration. A supplied Authorization header can never bypass the browser origin check.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RoomMutationAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (request.Headers.ContainsKey("Authorization"))
        {
            context.Result = Failure(403, "Room administration requires a browser session.");
            return;
        }
        if (context.HttpContext.User.Identity?.IsAuthenticated != true
            || string.IsNullOrWhiteSpace(context.HttpContext.User.FindFirst(AtprotoClaimTypes.Did)?.Value))
        {
            context.Result = Failure(401, "Sign in with your AT account before administering the site.");
            return;
        }
        if (!string.Equals(request.ContentType?.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = Failure(415, "Administrative requests must use application/json.");
            return;
        }
        var origins = request.Headers.Origin;
        if (origins.Count != 1 || !IsSameOrigin(origins[0], request.Scheme, request.Host.Value))
            context.Result = Failure(403, "Administrative requests must come from this site's origin.");
    }

    public static bool IsSameOrigin(string? origin, string scheme, string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || !Uri.TryCreate(origin, UriKind.Absolute, out var supplied)
            || !Uri.TryCreate(scheme + "://" + host, UriKind.Absolute, out var expected)
            || supplied.Scheme is not ("http" or "https") || supplied.UserInfo.Length != 0
            || supplied.AbsolutePath != "/" || supplied.Query.Length != 0 || supplied.Fragment.Length != 0)
            return false;
        return string.Equals(supplied.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(supplied.IdnHost, expected.IdnHost, StringComparison.OrdinalIgnoreCase)
            && supplied.Port == expected.Port;
    }

    private static ObjectResult Failure(int status, string title)
        => new(new ProblemDetails { Status = status, Title = title }) { StatusCode = status };
}
