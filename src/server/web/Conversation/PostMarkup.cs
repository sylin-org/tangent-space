using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Tangent.Conversation;

/// <summary>Read-time Markdown projection. Stored Post text remains the only source of content.</summary>
public static class PostMarkup
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().DisableHtml().Build();
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();
    public const int MaximumHtmlBytes = 512 * 1024;

    public static string? Html(PostContent content)
        => content.Format == PostContent.Markdown ? Render(content.Text) : null;

    /// <summary>Visible words for compact Post summaries, without Markdown link destinations or syntax.</summary>
    public static string PlainText(PostContent content)
    {
        if (content.Format != PostContent.Markdown) return content.Text;
        var rendered = Markdown.ToPlainText(content.Text, Pipeline);
        return string.Join(' ', rendered.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    public static string Render(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            var url = link.GetDynamicUrl is null ? link.Url : link.GetDynamicUrl();
            if (SafeUrl(url)) continue;
            link.Url = "#";
            link.GetDynamicUrl = null;
            link.IsImage = false;
        }
        var html = Sanitizer.Sanitize(document.ToHtml(Pipeline));
        if (Encoding.UTF8.GetByteCount(html) > MaximumHtmlBytes)
            throw new ArgumentException("Rendered Markdown exceeds the supported post size.");
        if (html.Contains("<img", StringComparison.OrdinalIgnoreCase)
            && html.Contains("<em", StringComparison.OrdinalIgnoreCase))
            html = FigureCaptions(html);
        if (Encoding.UTF8.GetByteCount(html) > MaximumHtmlBytes)
            throw new ArgumentException("Rendered Markdown exceeds the supported post size.");
        return html;
    }

    private static string FigureCaptions(string sanitizedHtml)
    {
        var document = new HtmlParser().ParseDocument("<body>" + sanitizedHtml + "</body>");
        foreach (var paragraph in document.QuerySelectorAll("p").ToArray())
        {
            var content = NonWhitespaceChildren(paragraph);
            if (content.Length == 0 || content[0] is not IElement { LocalName: "img" } image) continue;
            IElement? caption = null;
            IElement? following = null;
            if (content.Length == 2 && content[1] is IElement { LocalName: "em" } sameParagraph)
                caption = sameParagraph;
            else if (content.Length == 1 && paragraph.NextElementSibling is { LocalName: "p" } nextParagraph)
            {
                var nextContent = NonWhitespaceChildren(nextParagraph);
                if (nextContent.Length == 1 && nextContent[0] is IElement { LocalName: "em" } nextCaption)
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
        => element.ChildNodes.Where(node => node.NodeType != NodeType.Text
            || !string.IsNullOrWhiteSpace(node.TextContent)).ToArray();

    private static bool SafeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || value.Contains('\\')) return false;
        if (value.StartsWith("//", StringComparison.Ordinal)) return false;
        if (value.StartsWith("/", StringComparison.Ordinal) || value.StartsWith('#')) return true;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https" && !string.IsNullOrWhiteSpace(uri.Host);
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["p", "br", "hr", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote",
            "ul", "ol", "li", "pre", "code", "em", "strong", "del", "a", "img", "table", "thead", "tbody",
            "tr", "th", "td"]);
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
