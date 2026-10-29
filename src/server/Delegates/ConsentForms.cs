using Koan.Web.Auth.Server.Controllers;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Tangent.Render;

namespace Tangent.Delegates;

/// <summary>Only a person's session decides anything on Koan's authorization server: a request to its authorize
/// endpoint or its request endpoint that carries a bearer token is refused here before Koan sees it, so a delegate can
/// neither approve, deny, read a request nor reach a consent the person remembered for another client. The consent
/// page's approve and deny then answer to the same rule as every form post (<see cref="FormToken.Validate"/>).</summary>
public sealed class ConsentForms : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action
            || action.ControllerTypeInfo.AsType() != typeof(OAuthServerController)
            || action.ActionName is not (nameof(OAuthServerController.Authorize) or nameof(OAuthServerController.GetRequest)
                or nameof(OAuthServerController.Approve) or nameof(OAuthServerController.Deny)))
            return;

        if (DelegateIdentity.CarriesBearer(context.HttpContext))
            context.Result = ConsentController.NotByDelegate().ToResult();
        else if (action.ActionName is nameof(OAuthServerController.Approve) or nameof(OAuthServerController.Deny)
                 && !await FormToken.Validate(context.HttpContext))
            context.Result = new Problem(StatusCodes.Status400BadRequest, "Open the form again",
                "This form’s token is missing or has expired, so nothing was approved or denied.").ToResult();
    }
}
