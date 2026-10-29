using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Tangent.Space;

/// <summary>Marks a controller of the tree that answers as itself before first run: sign-in and first run itself.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AnswersBeforeFirstRunAttribute : Attribute;

/// <summary>Until the Tangent Space is established, every page of the tree answers the first-run resource, as HTML or
/// JSON by <c>Accept</c>, whatever its address and method. Only the tree's own controllers are concerned, so Koan's
/// routes (health, sign-in's challenge and callback) and static files answer as they are, and so does a controller
/// marked <see cref="AnswersBeforeFirstRunAttribute"/>. One bounded read of the row by its key per request.</summary>
public sealed class FirstRunGate(KoanWebBaseUrl baseUrl) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor action
            && action.ControllerTypeInfo.Assembly == typeof(FirstRunGate).Assembly
            && !action.ControllerTypeInfo.IsDefined(typeof(AnswersBeforeFirstRunAttribute), inherit: false)
            && await TangentSpace.Get(TangentSpace.SingleRow, context.HttpContext.RequestAborted) is null)
        {
            context.Result = FirstRunController.Answer(context.HttpContext, baseUrl);
            return;
        }

        await next();
    }
}
