using Koan.Web.Auth.Server.Controllers;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Tangent.Render;

namespace Tangent.Delegates;

/// <summary>Koan's approve and deny, which the consent page's forms post to, answer to the same rule as every form post
/// (<see cref="FormToken.Validate"/>): Koan decides them by the person's session alone and asks for no form token, so a
/// session's post without the page's token is refused here before Koan sees it.</summary>
public sealed class ConsentForms : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action
            || action.ControllerTypeInfo.AsType() != typeof(OAuthServerController)
            || action.ActionName is not (nameof(OAuthServerController.Approve) or nameof(OAuthServerController.Deny))
            || await FormToken.Validate(context.HttpContext))
            return;

        context.Result = new Problem(StatusCodes.Status400BadRequest, "Open the form again",
            "This form’s token is missing or has expired, so nothing was approved or denied.").ToResult();
    }
}
