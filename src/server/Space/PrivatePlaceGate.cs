using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Tangent.Participants;
using Tangent.Posts;

namespace Tangent.Space;

/// <summary>Marks a controller of the tree that answers a visitor of a private place as itself: sign-in, first run, and
/// the descriptor and <c>llms.txt</c>, which name nobody there.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AnswersEveryVisitorAttribute : Attribute;

/// <summary>A private place (<see cref="Audience.Private"/>) asks search engines to index none of it, by
/// <c>X-Robots-Tag: noindex</c> on every page of the tree, and serves a visitor only the sign-in: every page of the tree
/// answers them the sign-in page, which brings them back to the address they asked for, or for JSON the problem whose
/// gate is <c>sign-in</c>, both <c>401</c>, whatever the address and method. A controller marked
/// <see cref="AnswersEveryVisitorAttribute"/> answers as itself; Koan's routes and static files answer as they are. One
/// bounded read of the row by its key per request.</summary>
public sealed class PrivatePlaceGate(KoanWebBaseUrl baseUrl) : IAsyncResourceFilter
{
    /// <summary>The header that asks search engines not to index a page.</summary>
    public const string RobotsHeader = "X-Robots-Tag";

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (context.ActionDescriptor is ControllerActionDescriptor action
            && action.ControllerTypeInfo.Assembly == typeof(PrivatePlaceGate).Assembly
            && await TangentSpace.Get(TangentSpace.SingleRow, http.RequestAborted) is { Audience: Audience.Private } space)
        {
            http.Response.Headers[RobotsHeader] = "noindex";
            if (PostWrites.PersonId(http.User) is null && !action.ControllerTypeInfo.IsDefined(typeof(AnswersEveryVisitorAttribute), inherit: false))
            {
                context.Result = SignInController.Answer(http, baseUrl, space);
                return;
            }
        }

        await next();
    }
}
