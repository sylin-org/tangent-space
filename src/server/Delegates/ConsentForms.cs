using Koan.Web.Auth.Server.Controllers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Tangent.Render;

namespace Tangent.Delegates;

/// <summary>Koan's approve and deny, which the consent page's forms post to, always take the form's antiforgery token.
/// Koan decides them by the person's session cookie alone, an ambient credential, and asks for no token itself; so a
/// post without the page's token is refused here before Koan sees it, whatever bearer the request also carries, since a
/// bearer never decides there and so never stands in for the token.</summary>
public sealed class ConsentForms : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor action
            || action.ControllerTypeInfo.AsType() != typeof(OAuthServerController)
            || action.ActionName is not (nameof(OAuthServerController.Approve) or nameof(OAuthServerController.Deny)))
            return;

        try
        {
            await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new Problem(StatusCodes.Status400BadRequest, "Open the form again",
                "This form’s token is missing or has expired, so nothing was approved or denied.").ToResult();
        }
    }
}
