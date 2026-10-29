using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Tangent.Render;

/// <summary>A resource's response varies by <c>Accept</c> and the fragment header, carries a weak <c>ETag</c>
/// from the resource's version, and is a <c>304</c> when <c>If-None-Match</c> already holds that version.
/// The tag is weak because the HTML, the fragment and the JSON are three representations of one version.</summary>
public sealed class ETagFilter(ResourceComponents components) : IAsyncResultFilter
{
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: { } value } result && components.Has(value.GetType()))
        {
            var request = context.HttpContext.Request;
            var response = context.HttpContext.Response;
            response.Headers.Vary = new StringValues([HeaderNames.Accept, HtmlFormatter.FragmentHeader]);
            if (components.For(value.GetType()).Version is { } version
                && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                && result.StatusCode is null or (>= 200 and < 300))
            {
                var tag = new EntityTagHeaderValue($"\"{version(value)}\"", isWeak: true);
                response.GetTypedHeaders().ETag = tag;
                if (request.GetTypedHeaders().IfNoneMatch.Any(held => held.Equals(EntityTagHeaderValue.Any) || held.Compare(tag, useStrongComparison: false)))
                    context.Result = new StatusCodeResult(StatusCodes.Status304NotModified);
            }
        }
        return next();
    }
}
