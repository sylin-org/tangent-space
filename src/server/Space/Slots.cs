using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using Tangent.Posts;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>The proprietor's own HTML in the five places a page renders it, identical on the site, in the archive and in
/// exports. Each is sanitized when it is written and rendered as it stands after that, so a slot holds only what its
/// policy keeps.
/// <para>A slot can never run a script or reach a reader's session. Every policy is the sanitizer a Post's body goes through,
/// http and https its only schemes, so no <c>script</c>, no event handler and no <c>javascript:</c> link survives. None keeps
/// <c>form</c>, <c>input</c>, <c>base</c>, <c>iframe</c> or <c>svg</c>, nor <c>id</c>, <c>style</c> or a <c>data-</c> attribute, and
/// only a head's <c>meta</c> keeps <c>name</c>: a slot can post nothing with a reader's token, move where a page's links and
/// forms lead, clobber what a script looks up, or start an island. <c>class</c> is kept, which only names what CSS reads.</para>
/// <para>A slot's bound is on what it keeps, so what it already holds is never refused when a form sends it back.</para></summary>
public sealed partial record Slots
{
    public const int HeadLimit = 16000;
    public const int MastheadLimit = 2000;
    public const int DoorIntroLimit = 8000;
    public const int ArticleHeaderLimit = 8000;
    public const int FooterLimit = 2000;

    public const string HeadRule = "The head holds at most 16000 characters.";
    public const string MastheadRule = "The masthead holds at most 2000 characters.";
    public const string DoorIntroRule = "The door intro holds at most 8000 characters.";
    public const string ArticleHeaderRule = "The article header holds at most 8000 characters.";
    public const string FooterRule = "The footer holds at most 2000 characters.";

    /// <summary>A masthead and a footer are a line: a logo, a few words and some links, with no heading to compete with the
    /// page's own.</summary>
    private static readonly HtmlSanitizer Bar = MarkdownHtml.CreateSanitizer(
        ["a", "img", "span", "small", "em", "strong", "br", "div", "nav", "p"],
        [.. MarkdownHtml.BodyAttributes, "class", "width", "height"]);

    /// <summary>A door intro and an article header are prose, as a Post's body is, with what a designed opening adds.</summary>
    private static readonly HtmlSanitizer Prose = MarkdownHtml.CreateSanitizer(
        [.. MarkdownHtml.BodyTags, "div", "span", "small", "figure", "figcaption"],
        [.. MarkdownHtml.BodyAttributes, "class", "width", "height"]);

    /// <summary>A head holds what a page says about itself to other machines, and the place's look, and nothing else:
    /// <list type="bullet">
    /// <item><c>meta</c> by <c>name</c> or <c>property</c>, for a site's verification, a theme colour or a credit; never
    /// <c>http-equiv</c>, which redirects a page or sets its cookies and policies, nor the name <c>referrer</c>, which would
    /// hand every page's address to wherever it links.</item>
    /// <item><c>link</c> for icons and <c>rel="me"</c> alone; never a stylesheet, whose rules the place cannot read, nor a
    /// preload. <c>base</c> is not kept: it would carry every relative link and form, with a reader's token, elsewhere.</item>
    /// <item><c>style</c>, as <see cref="Fetchless"/> says, since a slot's HTML has no other way to look like the place.</item>
    /// </list>
    /// Text has no place in a head, whose end it would be.</summary>
    private static readonly HtmlSanitizer HeadElements = CreateHeadElements();

    private static readonly HashSet<string> LinkRelations = new(["icon", "shortcut", "apple-touch-icon", "me"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The CSS functions a head's <c>style</c> may call: values, gradients, transforms, filters and selectors, none
    /// of which fetches anything.</summary>
    private static readonly HashSet<string> CssFunctions = new(
    [
        "var", "calc", "min", "max", "clamp", "rgb", "rgba", "hsl", "hsla", "hwb", "lab", "lch", "oklab", "oklch", "color", "color-mix", "light-dark",
        "linear-gradient", "radial-gradient", "conic-gradient", "repeating-linear-gradient", "repeating-radial-gradient", "repeating-conic-gradient",
        "translate", "translatex", "translatey", "rotate", "scale", "skew", "cubic-bezier", "steps", "minmax", "repeat", "fit-content",
        "blur", "brightness", "contrast", "drop-shadow", "grayscale", "opacity", "saturate", "sepia",
        "not", "is", "where", "nth-child", "nth-last-child", "nth-of-type", "nth-last-of-type", "lang", "dir", "media"
    ], StringComparer.OrdinalIgnoreCase);

    public string Head { get; set; } = "";

    public string Masthead { get; set; } = "";

    public string DoorIntro { get; set; } = "";

    public string ArticleHeader { get; set; } = "";

    public string Footer { get; set; } = "";

    /// <summary>Whether <paramref name="reason"/> is a slot's refusal.</summary>
    public static bool IsRule(string reason)
        => reason is HeadRule or MastheadRule or DoorIntroRule or ArticleHeaderRule or FooterRule;

    /// <summary>These slots with what is written in them, sanitized, or a refusal with the rule of a slot that keeps more
    /// than its bound; one left null stays as it is.</summary>
    public Slots Written(string? head, string? masthead, string? doorIntro, string? articleHeader, string? footer) => new()
    {
        Head = Kept(head, Head, HeadOf, HeadLimit, HeadRule),
        Masthead = Kept(masthead, Masthead, html => Bar.Sanitize(html), MastheadLimit, MastheadRule),
        DoorIntro = Kept(doorIntro, DoorIntro, html => Prose.Sanitize(html), DoorIntroLimit, DoorIntroRule),
        ArticleHeader = Kept(articleHeader, ArticleHeader, html => Prose.Sanitize(html), ArticleHeaderLimit, ArticleHeaderRule),
        Footer = Kept(footer, Footer, html => Bar.Sanitize(html), FooterLimit, FooterRule)
    };

    private static string Kept(string? written, string held, Func<string, string> sanitized, int limit, string rule)
    {
        if (written is null) return held;
        var kept = sanitized(written).Trim();
        return kept.Length <= limit ? kept : throw new Refusal(rule);
    }

    /// <summary>A head as written, keeping what <see cref="HeadElements"/> says: its elements, then each <c>style</c> that
    /// fetches nothing, whose text no sanitizer would leave as CSS can read it.</summary>
    private static string HeadOf(string written)
    {
        using var document = new HtmlParser().ParseDocument("<!doctype html><html><body>" + written);
        var styles = document.QuerySelectorAll("style").ToList();
        var kept = styles.Select(style => style.TextContent.Trim()).Where(Fetchless).Select(css => "<style>\n" + css + "\n</style>").ToList();
        foreach (var style in styles) style.Remove();
        var elements = HeadElements.Sanitize(document.Body!.InnerHtml).Trim();
        return string.Join("\n", elements.Length > 0 ? kept.Prepend(elements) : kept);
    }

    /// <summary>Whether CSS can reach nothing outside the page. A rule's selector can read a form's token from its
    /// attribute, so CSS that can fetch can send that token away, one request per character. CSS that fetches nothing can
    /// restyle what the proprietor already owns and tell no one: so <c>@media</c> is its only at-rule, it calls only
    /// <see cref="CssFunctions"/>, which leaves out <c>url()</c>, <c>image-set()</c> and <c>src()</c> and any function a browser
    /// adds later, and it holds no escape, which could spell one of those, and no <c>&lt;</c>, which could end the element.
    /// <c>:has()</c> is left out too: without it what a selector reads of a token stays with that field and its siblings,
    /// and cannot show an image elsewhere on the page, whose lazy load would be a fetch. One that fails is taken out
    /// whole.</summary>
    private static bool Fetchless(string css)
        => css.Length > 0
           && css.IndexOfAny(['\\', '<']) < 0
           && AtRule().Matches(css).All(rule => rule.Value.Equals("@media", StringComparison.OrdinalIgnoreCase))
           && CssFunction().Matches(css).All(call => CssFunctions.Contains(call.Groups[1].Value));

    private static HtmlSanitizer CreateHeadElements()
    {
        var sanitizer = MarkdownHtml.CreateSanitizer(["meta", "link"], ["name", "property", "content", "rel", "href", "type", "sizes"]);
        sanitizer.PostProcessDom += (_, args) =>
        {
            foreach (var node in args.Document.Body!.ChildNodes.ToList())
                if (node is IElement element ? !BelongsInHead(element) : !string.IsNullOrWhiteSpace(node.TextContent))
                    node.Parent!.RemoveChild(node);
        };
        return sanitizer;
    }

    private static bool BelongsInHead(IElement element) => element.LocalName switch
    {
        "meta" => element.HasAttribute("content")
                  && (element.HasAttribute("property") || element.HasAttribute("name"))
                  && !string.Equals(element.GetAttribute("name")?.Trim(), "referrer", StringComparison.OrdinalIgnoreCase),
        "link" => !string.IsNullOrWhiteSpace(element.GetAttribute("href"))
                  && element.GetAttribute("rel")?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } relations
                  && relations.All(LinkRelations.Contains),
        _ => false
    };

    [GeneratedRegex(@"@[-\w]*", RegexOptions.CultureInvariant)]
    private static partial Regex AtRule();

    [GeneratedRegex(@"([-\w]+)\(", RegexOptions.CultureInvariant)]
    private static partial Regex CssFunction();
}
