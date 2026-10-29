using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Tangent.Posts;

/// <summary>Markdown to HTML that is safe to place in a page, and to plain text. CommonMark with pipe tables; raw HTML
/// in the source is shown as text. A link or image whose target is not safe — anything but an absolute
/// <c>http</c> or <c>https</c> URL with a host, a root-relative path or a fragment, and never with a control character,
/// a backslash or a leading <c>//</c> — points at <c>#</c>, and such an image becomes a link. What Markdig writes then
/// passes an allow-list: paragraphs, breaks, rules, headings, quotes, lists, code, emphasis, strikethrough, links,
/// images and tables; the attributes <c>href</c>, <c>src</c>, <c>alt</c> and <c>title</c>; the schemes <c>http</c>
/// and <c>https</c>; no style. Every image loads lazily and sends no referrer. An image followed by emphasis alone, in
/// its paragraph or the next, becomes a figure with that caption. A caller may rewrite or unwrap links by their target
/// before any of this (<see cref="Render"/>). Pure: no I/O, no state.</summary>
public static class MarkdownHtml
{
    /// <summary>The most UTF-8 bytes rendered HTML may hold, before and after figures are made.</summary>
    public const int HtmlLimit = 512 * 1024;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().DisableHtml().Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    /// <summary>What a caller makes of a link: <see cref="Url"/> its new target, or null to leave its words in place
    /// without a link; <see cref="Words"/> the words that replace its own, or null to keep them.</summary>
    public sealed record Link(string? Url, string? Words);

    /// <summary>The HTML for <paramref name="markdown"/>. <paramref name="links"/> is asked about each link that is not an
    /// image, by its target, and answers null to leave it as written. A rewritten target is held to the same safety as
    /// any other.</summary>
    public static string Render(string markdown, Func<string, Link?>? links = null)
    {
        var html = Sanitized(markdown, links);
        return Figured(html) ? FigureCaptions(html) : html;
    }

    /// <summary>Whether <paramref name="markdown"/> renders at all — Markdig refuses elements nested deeper than its
    /// limit — and within <see cref="HtmlLimit"/>, before and after figures are made. Markdown that fits renders and
    /// projects without a throw.</summary>
    public static bool Fits(string markdown)
    {
        try
        {
            var html = Sanitized(markdown, null);
            if (Encoding.UTF8.GetByteCount(html) > HtmlLimit) return false;
            return !Figured(html) || Encoding.UTF8.GetByteCount(FigureCaptions(html)) <= HtmlLimit;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The visible words of <paramref name="markdown"/> on one line: no syntax, no link targets, runs of
    /// whitespace as one space.</summary>
    public static string PlainText(string markdown)
    {
        var rendered = Markdown.ToPlainText(markdown, Pipeline);
        return string.Join(' ', rendered.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The targets of the links in <paramref name="markdown"/> that are not images, in order; a link inside code
    /// is code, not a link.</summary>
    public static IReadOnlyList<string> Links(string markdown)
        => Markdown.Parse(markdown, Pipeline).Descendants<LinkInline>()
            .Where(link => !link.IsImage)
            .Select(Target).OfType<string>().ToList();

    private static string? Target(LinkInline link) => link.GetDynamicUrl is null ? link.Url : link.GetDynamicUrl();

    private static string Sanitized(string markdown, Func<string, Link?>? links)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        if (links is not null)
        {
            foreach (var link in document.Descendants<LinkInline>().Where(link => !link.IsImage).ToList())
            {
                if (Target(link) is not { } target || links(target) is not { } rewrite) continue;
                if (rewrite.Words is { } words)
                {
                    while (link.FirstChild is { } child) child.Remove();
                    link.AppendChild(new LiteralInline(words));
                }

                if (rewrite.Url is { } url)
                {
                    link.Url = url;
                    link.GetDynamicUrl = null;
                }
                else
                {
                    Unwrap(link);
                }
            }
        }

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (SafeUrl(Target(link))) continue;
            link.Url = "#";
            link.GetDynamicUrl = null;
            link.IsImage = false;
        }

        return Sanitizer.Sanitize(document.ToHtml(Pipeline));
    }

    /// <summary>Puts a link's words where the link was, without the link.</summary>
    private static void Unwrap(LinkInline link)
    {
        while (link.FirstChild is { } child)
        {
            child.Remove();
            link.InsertBefore(child);
        }

        link.Remove();
    }

    /// <summary>Whether the HTML may hold an image with a caption: both an image and emphasis occur in it.</summary>
    private static bool Figured(string html)
        => html.Contains("<img", StringComparison.OrdinalIgnoreCase) && html.Contains("<em", StringComparison.OrdinalIgnoreCase);

    private static string FigureCaptions(string sanitizedHtml)
    {
        var document = new HtmlParser().ParseDocument("<body>" + sanitizedHtml + "</body>");
        foreach (var paragraph in document.QuerySelectorAll("p").ToArray())
        {
            var children = NonWhitespaceChildren(paragraph);
            if (children.Length == 0 || children[0] is not IElement { LocalName: "img" } image) continue;
            IElement? caption = null;
            IElement? following = null;
            if (children.Length == 2 && children[1] is IElement { LocalName: "em" } sameParagraph)
            {
                caption = sameParagraph;
            }
            else if (children.Length == 1 && paragraph.NextElementSibling is { LocalName: "p" } nextParagraph)
            {
                var next = NonWhitespaceChildren(nextParagraph);
                if (next.Length == 1 && next[0] is IElement { LocalName: "em" } nextCaption)
                {
                    caption = nextCaption;
                    following = nextParagraph;
                }
            }

            if (caption is null) continue;
            var figure = document.CreateElement("figure");
            var figcaption = document.CreateElement("figcaption");
            figure.AppendChild(image);
            figcaption.AppendChild(caption);
            figure.AppendChild(figcaption);
            paragraph.ParentElement?.ReplaceChild(figure, paragraph);
            following?.Remove();
        }

        return document.Body?.InnerHtml ?? sanitizedHtml;
    }

    private static INode[] NonWhitespaceChildren(IElement element)
        => element.ChildNodes.Where(node => node.NodeType != NodeType.Text || !string.IsNullOrWhiteSpace(node.TextContent)).ToArray();

    private static bool SafeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || value.Contains('\\')) return false;
        if (value.StartsWith("//", StringComparison.Ordinal)) return false;
        if (value.StartsWith('/') || value.StartsWith('#')) return true;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !string.IsNullOrWhiteSpace(uri.Host);
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "ul", "ol", "li", "pre", "code",
            "em", "strong", "del", "a", "img", "table", "thead", "tbody", "tr", "th", "td"]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(["href", "src", "alt", "title"]);
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["http", "https"]);
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.PostProcessNode += (_, args) =>
        {
            if (args.Node is not IElement { LocalName: "img" } image) return;
            image.SetAttribute("loading", "lazy");
            image.SetAttribute("decoding", "async");
            image.SetAttribute("referrerpolicy", "no-referrer");
        };
        return sanitizer;
    }
}
