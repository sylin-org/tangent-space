using System.Text;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc.Formatters;
using Tangent.Space;

namespace Tangent.Render;

/// <summary>Writes a resource as HTML: its component inside the shell, or the component alone when the request
/// carries the fragment header. It claims only the types a component is registered for, so every other
/// response keeps Koan's JSON. Components render statically through <see cref="HtmlRenderer"/>.</summary>
public sealed class HtmlFormatter : TextOutputFormatter
{
    /// <summary>The request header that asks for the component alone, without the shell. Its presence is
    /// the request; its value is not read.</summary>
    public const string FragmentHeader = "Tangent-Fragment";

    /// <summary>The stylesheet every page loads, under the base URL.</summary>
    public const string Stylesheet = "tangent.css";

    /// <summary>The folder the stylesheet's fonts are in, beside it.</summary>
    public const string Fonts = "fonts";

    /// <summary>The parameter every resource component declares for the value it renders.</summary>
    private const string ModelParameter = "Model";

    private readonly ResourceComponents _components;
    private readonly KoanWebBaseUrl _baseUrl;

    public HtmlFormatter(ResourceComponents components, KoanWebBaseUrl baseUrl)
    {
        _components = components;
        _baseUrl = baseUrl;
        SupportedMediaTypes.Add("text/html");
        SupportedEncodings.Add(Encoding.UTF8);
    }

    public static bool IsFragment(HttpRequest request) => request.Headers.ContainsKey(FragmentHeader);

    protected override bool CanWriteType(Type? type) => type is not null && _components.Has(type);

    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        var http = context.HttpContext;
        var value = context.Object!;
        var resource = _components.For(context.ObjectType!);
        var renderer = http.RequestServices.GetRequiredService<HtmlRenderer>();
        var fragment = IsFragment(http.Request);
        var accent = fragment ? "" : await Appearance.OfRequest(http);
        var html = fragment
            ? await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var alone = ParameterView.FromDictionary(new Dictionary<string, object?> { [ModelParameter] = value });
                return (await renderer.RenderComponentAsync(resource.Component, alone)).ToHtmlString();
            })
            : await Whole(renderer, resource, value, _baseUrl.Resolve(http), accent, resource.Frame?.Invoke(value));
        await http.Response.WriteAsync(html, selectedEncoding, http.RequestAborted);
    }

    /// <summary>A resource as a whole page: its component in the shell and the <paramref name="frame"/> around it, its files
    /// at <paramref name="baseUrl"/>. A <paramref name="standalone"/> page opens with no server and counts on no
    /// script.</summary>
    public static Task<string> Whole(HtmlRenderer renderer, ResourceComponent resource, object value, string baseUrl, string accent, Frame? frame,
        bool standalone = false) => renderer.Dispatcher.InvokeAsync(async () =>
    {
        var shell = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(Shell.Title)] = resource.Title(value),
            [nameof(Shell.BaseUrl)] = baseUrl,
            [nameof(Shell.Accent)] = accent,
            [nameof(Shell.Frame)] = frame,
            [nameof(Shell.Standalone)] = standalone,
            [nameof(Shell.ChildContent)] = (RenderFragment)(builder =>
            {
                builder.OpenComponent(0, resource.Component);
                builder.AddComponentParameter(1, ModelParameter, value);
                builder.CloseComponent();
            })
        });
        return (await renderer.RenderComponentAsync<Shell>(shell)).ToHtmlString();
    });
}
