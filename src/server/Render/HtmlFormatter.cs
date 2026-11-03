using System.Text;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Koan.Web.OpenGraph;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Primitives;
using Tangent.Access;
using Tangent.Space;

namespace Tangent.Render;

/// <summary>Writes a resource as HTML: its component inside the shell, or the component alone when the request
/// carries the fragment header. It claims only the types a component is registered for, so every other
/// response keeps Koan's JSON, and a refusal, which Koan answers as <c>application/problem+json</c>, only for a request
/// that prefers a page. Components render statically through <see cref="HtmlRenderer"/>.</summary>
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
    private readonly IOpenGraphCardRenderer _cards;

    public HtmlFormatter(ResourceComponents components, KoanWebBaseUrl baseUrl, IOpenGraphCardRenderer cards)
    {
        _components = components;
        _baseUrl = baseUrl;
        _cards = cards;
        SupportedMediaTypes.Add("text/html");
        SupportedEncodings.Add(Encoding.UTF8);
    }

    public static bool IsFragment(HttpRequest request) => request.Headers.ContainsKey(FragmentHeader);

    protected override bool CanWriteType(Type? type) => type is not null && _components.Has(type);

    /// <summary>A refusal reads as a page where the request ranks HTML above everything else it names; otherwise it
    /// stays Koan's problem JSON.</summary>
    public override bool CanWriteResult(OutputFormatterCanWriteContext context)
    {
        if (context.Object is not Problem) return base.CanWriteResult(context);
        if (!PrefersHtml(context.HttpContext.Request)) return false;
        context.ContentType = new StringSegment("text/html");
        return true;
    }

    private static bool PrefersHtml(HttpRequest request)
    {
        var accept = request.GetTypedHeaders().Accept;
        var html = accept.Where(type => type.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase)).Select(type => type.Quality ?? 1).DefaultIfEmpty(0).Max();
        return html > 0 && accept.All(type => (type.Quality ?? 1) <= html);
    }

    public override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
    {
        var http = context.HttpContext;
        var value = context.Object!;
        var resource = _components.For(context.ObjectType!);
        var renderer = http.RequestServices.GetRequiredService<HtmlRenderer>();
        var fragment = IsFragment(http.Request);
        var card = fragment || resource.Card?.Invoke(value) is not { } values ? null : _cards.RenderHead(values, http.Request);
        var also = value is Problem { Gate: var gate } && gate == Capabilities.SignIn.Word
            ? new Dictionary<string, object?> { [nameof(ProblemView.SignIn)] = SignInStep(http) }
            : null;
        var html = fragment
            ? await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var alone = ParameterView.FromDictionary(new Dictionary<string, object?>(also ?? []) { [ModelParameter] = value });
                return (await renderer.RenderComponentAsync(resource.Component, alone)).ToHtmlString();
            })
            : await WholeForRequest(http, renderer, resource, value, card, also);
        await http.Response.WriteAsync(html, selectedEncoding, http.RequestAborted);
    }

    /// <summary>Where signing in starts under the base URL; the address it comes back to is that of a <c>GET</c>.</summary>
    private string SignInStep(HttpContext http)
    {
        var address = _baseUrl.Resolve(http) + Participants.SignInController.Path;
        return HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)
            ? address + "?return=" + Uri.EscapeDataString(http.Request.PathBase + http.Request.Path + http.Request.QueryString)
            : address;
    }

    private async Task<string> WholeForRequest(HttpContext http, HtmlRenderer renderer, ResourceComponent resource, object value, string? card,
        IReadOnlyDictionary<string, object?>? also)
    {
        var space = await TangentSpace.OfRequest(http);
        var address = _baseUrl.Resolve(http);
        var frame = value is Problem problem
            ? ProblemView.Frame(problem, space.Name, address + "/", http.User.Identity?.IsAuthenticated == true ? null : address + Participants.SignInController.Path)
            : resource.Frame?.Invoke(value);
        return await Whole(renderer, resource, value, address, Appearance.Word(space.Appearance.Accent), space.Slots.Placed(address + "/"), frame, card: card, also: also);
    }

    /// <summary>A resource as a whole page: its component in the shell and the <paramref name="frame"/> around it, its files
    /// at <paramref name="baseUrl"/>, with the place's <paramref name="accent"/> and <paramref name="slots"/>. A
    /// <paramref name="standalone"/> page opens with no server and counts on no script; <paramref name="retired"/> is the
    /// line that says the place is retired, on a page of its archive. The component reads <paramref name="also"/>, where
    /// given, beside its value.</summary>
    public static Task<string> Whole(HtmlRenderer renderer, ResourceComponent resource, object value, string baseUrl, string accent, Slots slots, Frame? frame,
        bool standalone = false, string? card = null, string? retired = null, IReadOnlyDictionary<string, object?>? also = null) =>
        renderer.Dispatcher.InvokeAsync(async () =>
    {
        var shell = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(Shell.Title)] = resource.Title(value),
            [nameof(Shell.BaseUrl)] = baseUrl,
            [nameof(Shell.Accent)] = accent,
            [nameof(Shell.Slots)] = slots,
            [nameof(Shell.Card)] = card,
            [nameof(Shell.Frame)] = frame,
            [nameof(Shell.Standalone)] = standalone,
            [nameof(Shell.Retired)] = retired,
            [nameof(Shell.ChildContent)] = (RenderFragment)(builder =>
            {
                builder.OpenComponent(0, resource.Component);
                builder.AddComponentParameter(1, ModelParameter, value);
                foreach (var (name, given) in also ?? new Dictionary<string, object?>())
                    builder.AddComponentParameter(2, name, given);
                builder.CloseComponent();
            })
        });
        return (await renderer.RenderComponentAsync<Shell>(shell)).ToHtmlString();
    });
}
