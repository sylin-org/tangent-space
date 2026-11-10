using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Koan.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Tangent.Posts;

namespace Tangent.Space;

/// <summary>A person's read of an address a declared entity controller serves, with words in its query that its operation does not
/// take, as a link another site marked carries them (<c>fbclid</c>, <c>utm_source</c>), is sent on with a <c>302</c> to the same
/// address under the place's base with only the words it takes, which then answers its page. A sender that reads JSON keeps Koan's
/// refusal naming the word, and a partition named stays for the endpoint to refuse. The words an operation takes are Koan's
/// (<c>EntityOperation.Takes</c>, internal to Koan): its declared input, a keyset's <c>after</c> and <c>size</c>, a served partition.</summary>
public sealed class MarkedLinks(KoanWebBaseUrl baseUrl) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var http = context.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method)) return;
        if (context.ActionDescriptor is not ControllerActionDescriptor action || !EntityOperation.IsDeclared(action.ControllerTypeInfo)
            || !EntityOperation.IsEntityOperation(action.MethodInfo) || PostWrites.WantsJson(http.Request)) return;
        var operation = EntityOperation.Of(action.MethodInfo, action.ControllerTypeInfo);
        var query = http.Request.Query;
        if (query.Keys.All(word => Kept(operation, word))) return;
        var kept = string.Join("&", query.Where(pair => Kept(operation, pair.Key))
            .SelectMany(pair => pair.Value.Select(value => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(value ?? ""))));
        context.Result = new RedirectResult(baseUrl.Resolve(http) + http.Request.Path + (kept.Length > 0 ? "?" + kept : ""));
    }

    public void OnActionExecuted(ActionExecutedContext context) { }

    private static bool Kept(EntityOperation operation, string word)
        => string.Equals(word, KoanWebConstants.Query.Set, StringComparison.OrdinalIgnoreCase)
           || operation.InputMembers.Any(member => string.Equals(EntityOperation.WireName(member), word, StringComparison.OrdinalIgnoreCase)
                                                   || string.Equals(member.Name, word, StringComparison.OrdinalIgnoreCase))
           || operation.Keyset is not null && (string.Equals(word, KoanWebConstants.Query.After, StringComparison.OrdinalIgnoreCase)
                                               || string.Equals(word, KoanWebConstants.Query.Size, StringComparison.OrdinalIgnoreCase));
}
