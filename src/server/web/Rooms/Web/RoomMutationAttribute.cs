using Koan.Web.Auth.Connector.Atproto;
using TangentSpace.Participation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TangentSpace.Rooms.Web;

/// <summary>Cookie mutations require same origin; bearer mutations require an explicit transport grant and the domain still checks scope.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RoomMutationAttribute(string grant = ParticipationGrants.Manage) : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (request.Headers.ContainsKey("Authorization"))
        {
            try
            {
                var principal = context.HttpContext.User;
                if (!ParticipationAccess.UsesCredential(principal)) throw new UnauthorizedAccessException();
                ParticipationAccess.Require(principal, grant);
                if (!string.Equals(request.ContentType?.Split(';',2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
                    context.Result = Failure(415, "Use application/json.");
            }
            catch (UnauthorizedAccessException) { context.Result = Failure(403, "This credential does not permit the operation."); }
            return;
        }
        if (context.HttpContext.User.Identity?.IsAuthenticated != true
            || string.IsNullOrWhiteSpace(context.HttpContext.User.FindFirst(ParticipationConstants.ParticipantClaim)?.Value))
        {
            context.Result = Failure(401, "Sign in with your account before administering the space.");
            return;
        }
        if (!string.Equals(request.ContentType?.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
        {
            context.Result = Failure(415, "Administrative requests must use application/json.");
            return;
        }
        var origins = request.Headers.Origin;
        if (origins.Count != 1 || !IsSameOrigin(origins[0], request.Scheme, request.Host.Value))
            context.Result = Failure(403, "Administrative requests must come from this space's origin.");
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
