using Koan.Web.Auth.Server.Controllers;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Tangent.Render;

namespace Tangent.Delegates;

/// <summary>The consent page's approve and deny post to Koan's request endpoint, and answer to the same rule as every
/// form post (<see cref="FormToken.Validate"/>) before Koan sees them. Only the person approves or denies, from their
/// session: a delegate's bearer request is refused here.</summary>
public sealed class ConsentForms : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action
            || action.ControllerTypeInfo.AsType() != typeof(OAuthServerController)
            || action.ActionName is not (nameof(OAuthServerController.Approve) or nameof(OAuthServerController.Deny)))
            return;

        if (DelegateIdentity.Delegated(context.HttpContext))
            context.Result = ConsentController.NotByDelegate().ToResult();
        else if (!await FormToken.Validate(context.HttpContext))
            context.Result = new Problem(StatusCodes.Status400BadRequest, "Open the form again",
                "This form’s token is missing or has expired, so nothing was approved or denied.").ToResult();
    }
}
