using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Tangent.Posts;

/// <summary>Markdown to HTML that is safe to place in a page, and to plain text, from one parse. CommonMark with pipe
/// tables, and a single line break kept as a break; raw HTML in the source is shown as text. A link or image whose
/// target is not safe — anything but an absolute <c>http</c> or <c>https</c> URL with a host, a root-relative path or a
/// fragment, and never with a control character, a backslash or a leading <c>//</c> — points at <c>#</c>, and such an
/// image becomes a link. What Markdig writes then passes an allow-list: paragraphs, breaks, rules, headings, quotes,
/// lists, code, emphasis, strikethrough, links, images and tables; the attributes <c>href</c>, <c>src</c>, <c>alt</c> and
/// <c>title</c>; the schemes <c>http</c> and <c>https</c>; no style. Every image loads lazily and sends no referrer. An
/// image followed by emphasis alone, in its paragraph or the next, becomes a figure with that caption. A caller may
/// hold links by their target (<see cref="Render"/>): each is left out of the parse's result and given its target and
/// words when the result is written out (<see cref="Rendered.Html"/>, <see cref="Rendered.PlainText"/>), so one parse
/// serves every later answer. <see cref="Prose"/> says where in the source the words a reader reads as words stand.
/// <see cref="Tractable"/> refuses, by a linear count, the structures that make Markdig's parse grow with the square of
/// their size. Pure: no I/O.</summary>
public static class MarkdownHtml
{
    /// <summary>The most UTF-8 bytes rendered HTML may hold, every held link written at its longest.</summary>
    public const int HtmlLimit = 512 * 1024;

    /// <summary>The most UTF-8 bytes one held link is written in; a longer one is written as its own words.</summary>
    public const int LinkLimit = 4096;

    /// <summary>The most characters link destinations may scan in all: for each <c>](</c>, the run after it up to the next
    /// whitespace (to the line's end after <c>&lt;</c>).</summary>
    public const long DestinationWork = 1_000_000;

    /// <summary>The most the open brackets of the paragraphs may weigh: the sum of each paragraph's count, squared.</summary>
    public const long BracketWork = 4_000_000;

    /// <summary>The most the emphasis runs of the paragraphs may weigh: the sum of each paragraph's count of runs of
    /// <c>*</c> or <c>_</c>, squared.</summary>
    public const long EmphasisWork = 4_000_000;

    /// <summary>The most elements the HTML may hold, as the lines and marks outside fenced code count them: each line
    /// one, each quote or list marker opening it two, each table cell, bracket, emphasis run, code span and autolink
    /// one, and each fence two. The sanitizer's work grows faster than the elements it walks.</summary>
    public const long ElementLimit = 2000;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseSoftlineBreakAsHardlineBreak().DisableHtml().Build();

    /// <summary>The same parse, with every inline's place in the source.</summary>
    private static readonly MarkdownPipeline Located = new MarkdownPipelineBuilder()
        .UsePipeTables().UseSoftlineBreakAsHardlineBreak().DisableHtml().UsePreciseSourceLocation().Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    /// <summary>The target a held link carries through the parse: this process's own, which no input can know.</summary>
    private static readonly string HeldPrefix = "https://held.invalid/" + Guid.NewGuid().ToString("N") + "/";

    private static readonly Regex HeldAnchor = new("<a href=\"" + Regex.Escape(HeldPrefix) + "(\\d+)\">(.*?)</a>",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    /// <summary>A held link's place in the plain text: NUL never survives Markdig's parse, which makes it U+FFFD.</summary>
    private static readonly Regex HeldWords = new("\0(\\d+)\0", RegexOptions.CultureInvariant);

    private static readonly Regex Tags = new("<[^>]*>", RegexOptions.CultureInvariant);

    private static long _parses;

    /// <summary>How many times this process has parsed Markdown.</summary>
    public static long Parses => Interlocked.Read(ref _parses);

    /// <summary>What a caller writes for a held link: its target and its words.</summary>
    public sealed record Link(string Url, string Words);

    /// <summary>A piece of a rendered result: words as they stand, or a held link (<see cref="Held"/> its index) with
    /// its own words as the source wrote them.</summary>
    public sealed record Piece(string Text, int Held = -1);

    /// <summary>One parse's result: the HTML and the plain text in pieces, and the targets of the held links by index.
    /// <see cref="HtmlBytes"/> is what the HTML weighs with every held link as its own words; <see cref="Fits"/> says
    /// whether it stays within <see cref="HtmlLimit"/> with every held link written at <see cref="LinkLimit"/>.</summary>
    public sealed class Rendered(IReadOnlyList<Piece> html, IReadOnlyList<Piece> plain, IReadOnlyList<string> held)
    {
        public IReadOnlyList<string> Held { get; } = held;

        public long HtmlBytes { get; } = html.Sum(piece => (long)Encoding.UTF8.GetByteCount(piece.Text));

        public bool Fits => HtmlBytes + (long)Held.Count * LinkLimit <= HtmlLimit;

        /// <summary>The HTML, each held link written as <paramref name="link"/> answers for its target: an anchor to a safe
        /// URL with those words, within <see cref="LinkLimit"/>; otherwise, or where it answers null, its own words.</summary>
        public string Html(Func<string, Link?>? link = null)
        {
            var builder = new StringBuilder();
            foreach (var piece in html)
            {
                if (piece.Held >= 0 && link?.Invoke(Held[piece.Held]) is { } written && SafeUrl(written.Url))
                {
                    var anchor = "<a href=\"" + WebUtility.HtmlEncode(written.Url) + "\">" + WebUtility.HtmlEncode(written.Words) + "</a>";
                    if (Encoding.UTF8.GetByteCount(anchor) <= LinkLimit)
                    {
                        builder.Append(anchor);
                        continue;
                    }
                }

                builder.Append(piece.Text);
            }

            return builder.ToString();
        }

        /// <summary>The visible words on one line, each held link as <paramref name="words"/> answers for its target, or
        /// its own words: no syntax, no link targets, runs of whitespace as one space.</summary>
        public string PlainText(Func<string, string?>? words = null)
        {
            var builder = new StringBuilder();
            foreach (var piece in plain)
                builder.Append(piece.Held >= 0 && words?.Invoke(Held[piece.Held]) is { } replaced ? replaced : piece.Text);
            return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
    }

    /// <summary>Whether <paramref name="markdown"/> parses and renders in time proportional to its size: Markdig's parse
    /// within <see cref="DestinationWork"/>, <see cref="BracketWork"/> and <see cref="EmphasisWork"/>, and the HTML
    /// within <see cref="ElementLimit"/> elements, which also bounds how deep quotes and lists nest. Fenced code is not
    /// counted but its fences are; every other line is, and a paragraph runs to a blank line or a fence, which counts
    /// more than Markdig would and never less. One pass over the text.</summary>
    public static bool Tractable(string markdown)
    {
        long destinations = 0, bracketWork = 0, emphasisWork = 0, brackets = 0, runs = 0, elements = 0;
        char? fence = null;
        foreach (var line in markdown.Split('\n'))
        {
            if (elements > ElementLimit) return false;
            var start = 0;
            while (start < line.Length && start < 3 && line[start] == ' ') start++;
            var fenceLine = line.Length - start >= 3 && line[start] is '`' or '~' && line[start + 1] == line[start] && line[start + 2] == line[start];
            if (fenceLine || fence is not null || string.IsNullOrWhiteSpace(line))
            {
                bracketWork += brackets * brackets;
                emphasisWork += runs * runs;
                brackets = runs = 0;
                if (fenceLine)
                {
                    elements += 2;
                    fence = fence is null ? line[start] : fence == line[start] ? null : fence;
                }

                continue;
            }

            elements += 1 + 2 * Markers(line);

            // The index of the next whitespace at or after each position, so each destination is measured in one step.
            var next = new int[line.Length + 1];
            next[line.Length] = line.Length;
            for (var index = line.Length - 1; index >= 0; index--) next[index] = char.IsWhiteSpace(line[index]) ? index : next[index + 1];
            for (var index = 0; index < line.Length; index++)
            {
                var c = line[index];
                var first = index == 0 || line[index - 1] != c;
                if (c == '[')
                {
                    brackets++;
                    elements++;
                }
                else if (c is '*' or '_' && first)
                {
                    runs++;
                    elements++;
                }
                else if (c == '|' || c == '`' && first || c == '<' && index + 1 < line.Length && char.IsAsciiLetter(line[index + 1]))
                {
                    elements++;
                }
                else if (c == '(' && index > 0 && line[index - 1] == ']')
                {
                    var from = index + 1;
                    while (from < line.Length && line[from] is ' ' or '\t') from++;
                    destinations += from < line.Length && line[from] == '<' ? line.Length - from : next[from] - from;
                }
            }
        }

        bracketWork += brackets * brackets;
        emphasisWork += runs * runs;
        return destinations <= DestinationWork && bracketWork <= BracketWork && emphasisWork <= EmphasisWork && elements <= ElementLimit;
    }

    /// <summary>How many quote and list markers open a line, however they are indented: <c>&gt;</c>, a bullet
    /// (<c>-</c>, <c>*</c> or <c>+</c>) or an ordinal (digits, then <c>.</c> or <c>)</c>) followed by whitespace or the
    /// line's end.</summary>
    private static int Markers(string line)
    {
        var markers = 0;
        var at = 0;
        while (true)
        {
            while (at < line.Length && line[at] is ' ' or '\t') at++;
            if (at >= line.Length) return markers;
            var end = at;
            if (line[at] == '>')
            {
                end = at + 1;
            }
            else if (line[at] is '-' or '*' or '+')
            {
                end = at + 1;
                if (end < line.Length && line[end] is not (' ' or '\t')) return markers;
            }
            else if (char.IsAsciiDigit(line[at]))
            {
                while (end < line.Length && end - at < 9 && char.IsAsciiDigit(line[end])) end++;
                if (end >= line.Length || line[end] is not ('.' or ')')) return markers;
                end++;
                if (end < line.Length && line[end] is not (' ' or '\t')) return markers;
            }
            else
            {
                return markers;
            }

            markers++;
            at = end;
        }
    }

    /// <summary>Parses <paramref name="markdown"/> once into its HTML and plain text, holding each link whose target
    /// <paramref name="hold"/> answers true for — never an image, nor a link inside an image's description, which is
    /// only words of its <c>alt</c>. Throws <see cref="ArgumentException"/> where Markdig
    /// refuses elements nested deeper than its limit; ask <see cref="Tractable"/> first, since a refused parse costs as
    /// much as any.</summary>
    public static Rendered Render(string markdown, Func<string, bool>? hold = null)
    {
        Interlocked.Increment(ref _parses);
        var document = Markdown.Parse(markdown, Pipeline);
        var held = new List<(LinkInline Link, string Target)>();
        if (hold is not null)
        {
            foreach (var link in document.Descendants<LinkInline>().Where(link => !link.IsImage && !InImage(link)).ToList())
            {
                if (Target(link) is not { } target || !hold(target)) continue;
                link.Url = HeldPrefix + held.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                link.GetDynamicUrl = null;
                link.Title = null;
                held.Add((link, target));
            }
        }

        foreach (var link in document.Descendants<LinkInline>())
        {
            if (SafeUrl(Target(link))) continue;
            link.Url = "#";
            link.GetDynamicUrl = null;
            link.IsImage = false;
        }

        var html = Sanitizer.Sanitize(document.ToHtml(Pipeline));
        if (Encoding.UTF8.GetByteCount(html) <= HtmlLimit && Figured(html)) html = FigureCaptions(html);

        for (var index = 0; index < held.Count; index++)
        {
            var link = held[index].Link;
            while (link.FirstChild is { } child) child.Remove();
            link.AppendChild(new LiteralInline("\0" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\0"));
        }

        var labels = new Dictionary<int, string>();
        foreach (Match match in HeldAnchor.Matches(html)) labels.TryAdd(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), match.Groups[2].Value);
        return new Rendered(Pieces(html, HeldAnchor, match => match.Groups[2].Value), Pieces(Plain(document), HeldWords, match => Words(labels, match)),
            held.Select(entry => entry.Target).ToList());
    }

    /// <summary>Where <paramref name="markdown"/> holds its prose: each run of the source that reads as the words it
    /// is, in the order of the source — never code, a link's words or its target, an image's description, an autolink,
    /// or a character a backslash escapes. One parse; ask <see cref="Tractable"/> first, since Markdig throws
    /// <see cref="ArgumentException"/> past its nesting limit.</summary>
    public static IReadOnlyList<Range> Prose(string markdown)
    {
        Interlocked.Increment(ref _parses);
        var runs = new List<Range>();
        foreach (var literal in Markdown.Parse(markdown, Located).Descendants<LiteralInline>())
        {
            var span = literal.Span;
            if (InLink(literal) || span.IsEmpty || span.Start < 0 || span.End >= markdown.Length) continue;
            // A run whose source is not its words, such as an escape, is not prose as typed.
            if (!markdown.AsSpan(span.Start, span.Length).SequenceEqual(literal.Content.AsSpan())) continue;
            runs.Add(span.Start..(span.End + 1));
        }

        return runs;
    }

    /// <summary>Whether an inline sits inside a link or an image, among its words or its description.</summary>
    private static bool InLink(Inline inline)
    {
        for (var parent = inline.Parent; parent is not null; parent = parent.Parent)
            if (parent is LinkInline) return true;
        return false;
    }

    /// <summary>A held link's own words in the plain text: its words in the HTML, without their tags.</summary>
    private static string Words(IReadOnlyDictionary<int, string> labels, Match match)
    {
        var index = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return labels.TryGetValue(index, out var label) ? WebUtility.HtmlDecode(Tags.Replace(label, "")) : "";
    }

    private static List<Piece> Pieces(string text, Regex held, Func<Match, string> words)
    {
        var pieces = new List<Piece>();
        var at = 0;
        foreach (Match match in held.Matches(text))
        {
            if (match.Index > at) pieces.Add(new Piece(text[at..match.Index]));
            pieces.Add(new Piece(words(match), int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)));
            at = match.Index + match.Length;
        }

        if (at < text.Length) pieces.Add(new Piece(text[at..]));
        return pieces;
    }

    /// <summary>The document as plain text, the way Markdig writes it without HTML.</summary>
    private static string Plain(MarkdownDocument document)
    {
        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer) { EnableHtmlForBlock = false, EnableHtmlForInline = false, EnableHtmlEscape = false };
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    private static string? Target(LinkInline link) => link.GetDynamicUrl is null ? link.Url : link.GetDynamicUrl();

    /// <summary>Whether a link sits inside an image's description, where it is only words of the image's
    /// <c>alt</c>.</summary>
    private static bool InImage(Inline link)
    {
        for (var parent = link.Parent; parent is not null; parent = parent.Parent)
            if (parent is LinkInline { IsImage: true }) return true;
        return false;
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
            // The caption may follow on the image's own line or the next, where the line break is a <br> between them.
            if (children.Length == 2 && children[1] is IElement { LocalName: "em" } sameParagraph
                || children.Length == 3 && children[1] is IElement { LocalName: "br" } && children[2] is IElement { LocalName: "em" })
            {
                caption = (IElement)children[^1];
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
