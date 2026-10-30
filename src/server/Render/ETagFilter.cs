using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Tangent.Space;

namespace Tangent.Render;

/// <summary>A resource's response varies by <c>Accept</c> and the fragment header, carries a weak <c>ETag</c> from
/// the resource's version and the representation chosen for it, and is a <c>304</c> when <c>If-None-Match</c>
/// already holds that tag. One version has three tags, since a cache revalidates with every tag it holds for the
/// address. A whole page's tag also names the place's accent, which its shell carries, so a change of accent reaches a
/// page a cache already holds.</summary>
public sealed class ETagFilter(
    ResourceComponents components,
    OutputFormatterSelector selector,
    IHttpResponseStreamWriterFactory writers) : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { Value: { } value } result && components.Has(value.GetType()))
        {
            var request = context.HttpContext.Request;
            var response = context.HttpContext.Response;
            response.Headers.Vary = new StringValues([HeaderNames.Accept, HtmlFormatter.FragmentHeader]);
            if (components.For(value.GetType()).Version is { } version
                && (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
                && result.StatusCode is null or (>= 200 and < 300)
                && Representation(context.HttpContext, result, value) is { } representation)
            {
                var spelled = representation == "html" ? $"html.{await Appearance.OfRequest(context.HttpContext)}" : representation;
                var tag = new EntityTagHeaderValue($"\"{version(value)}.{spelled}\"", isWeak: true);
                response.GetTypedHeaders().ETag = tag;
                if (request.GetTypedHeaders().IfNoneMatch.Any(held => held.Equals(EntityTagHeaderValue.Any) || held.Compare(tag, useStrongComparison: false)))
                    context.Result = new StatusCodeResult(StatusCodes.Status304NotModified);
            }
        }
        await next();
    }

    /// <summary>The representation the formatter will choose for this request, as the tag spells it: <c>html</c>,
    /// <c>fragment</c>, or the media type's subtype, <c>json</c>; null when no formatter can answer.</summary>
    private string? Representation(HttpContext http, ObjectResult result, object value)
    {
        var write = new OutputFormatterWriteContext(http, writers.CreateWriter, result.DeclaredType ?? value.GetType(), value);
        if (selector.SelectFormatter(write, result.Formatters, result.ContentTypes) is null) return null;
        var mediaType = new MediaType(write.ContentType);
        if (mediaType.SubType.Equals("html", StringComparison.OrdinalIgnoreCase))
            return HtmlFormatter.IsFragment(http.Request) ? "fragment" : "html";
        return (mediaType.SubTypeSuffix.HasValue ? mediaType.SubTypeSuffix : mediaType.SubType).ToString().ToLowerInvariant();
    }
}
