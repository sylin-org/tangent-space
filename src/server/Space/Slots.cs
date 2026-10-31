using AngleSharp.Dom;
using Ganss.Xss;
using Tangent.Posts;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>The proprietor's own HTML in the five places a page renders it, identical on the site, in the archive and in
/// exports. Each is sanitized when it is written and rendered as it stands after that, so a slot holds only what its
/// policy keeps.
/// <para>A slot can never run a script or reach a reader's session. Every policy is the sanitizer a Post's body goes through,
/// http and https its only schemes, an image on the place's own origin only under <c>/media/</c>
/// (<see cref="MarkdownHtml.SafeImage"/>), so no <c>script</c>, no event handler and no <c>javascript:</c> link survives, and no
/// image signs a reader out by loading. None keeps <c>form</c>, <c>input</c>, <c>base</c>, <c>iframe</c> or <c>svg</c>, nor <c>id</c>
/// or a <c>data-</c> attribute, and only a head's <c>meta</c> keeps <c>name</c>: a slot can post nothing with a reader's token,
/// move where a page's links and forms lead, clobber what a script looks up, or start an island.</para>
/// <para>No slot holds CSS, as a <c>style</c> element, a <c>style</c> attribute or a stylesheet. A selector reads a form's token
/// from its attribute, and keywords alone can then bring a lazy image into view, which sends what was read to another host:
/// no list of allowed CSS closes that. <c>class</c> is kept, so a slot can wear the design's own classes, and they are all it
/// reaches: a script looks a class up only inside an <c>article</c>, a <c>main</c> or an island's own element, none of which a
/// slot can hold.</para>
/// <para>A slot's bound is on what it keeps, so what it already holds is never refused when a form sends it back.</para></summary>
public sealed record Slots
{
    public const int HeadLimit = 4000;
    public const int MastheadLimit = 2000;
    public const int DoorIntroLimit = 8000;
    public const int ArticleHeaderLimit = 8000;
    public const int FooterLimit = 2000;

    public const string HeadRule = "The head holds at most 4000 characters.";
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

    /// <summary>A head holds what a page says about itself to other machines, and nothing else:
    /// <list type="bullet">
    /// <item><c>meta</c> by <c>name</c> or <c>property</c>, for a site's verification, a theme colour or a credit; never
    /// <c>http-equiv</c>, which redirects a page or sets its cookies and policies, nor the name <c>referrer</c>, which would
    /// hand every page's address to wherever it links.</item>
    /// <item><c>link</c> for icons, which a browser loads as it does an image and so are judged as one, and <c>rel="me"</c>,
    /// which it never loads; never a stylesheet or a preload. <c>base</c> is not kept: it would carry every relative link and
    /// form, with a reader's token, elsewhere.</item>
    /// </list>
    /// Text has no place in a head, whose end it would be.</summary>
    private static readonly HtmlSanitizer HeadElements = CreateHeadElements();

    private const string Me = "me";

    private static readonly HashSet<string> LinkRelations = new(["icon", "shortcut", "apple-touch-icon", Me], StringComparer.OrdinalIgnoreCase);

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
        Head = Kept(head, Head, html => HeadElements.Sanitize(html), HeadLimit, HeadRule),
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
                  && relations.All(LinkRelations.Contains)
                  && (relations.All(relation => relation.Equals(Me, StringComparison.OrdinalIgnoreCase)) || MarkdownHtml.SafeImage(element.GetAttribute("href"))),
        _ => false
    };
}
