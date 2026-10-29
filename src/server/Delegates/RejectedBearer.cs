using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Tangent.Render;

namespace Tangent.Delegates;

/// <summary>A page of the tree asked with a bearer token that authenticated nobody answers that, before anything
/// else, rather than reading as a visitor's: the agent learns that its token is expired or not this Tangent Space's
/// and asks for a new one. Koan's own routes answer as they are.</summary>
public sealed class RejectedBearer : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action
            || action.ControllerTypeInfo.Assembly != typeof(RejectedBearer).Assembly
            || !DelegateIdentity.Rejected(context.HttpContext))
            return;

        context.HttpContext.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
        context.Result = new Problem(StatusCodes.Status401Unauthorized, "Token not accepted",
            "This request’s bearer token is expired, or was not issued for this Tangent Space; ask for a new one.").ToResult();
    }
}
