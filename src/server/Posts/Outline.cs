using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Tangent.Posts;

/// <summary>An article's outline: its second- and third-level headings, read from the safe HTML its body renders to, each
/// given an id the page can be opened at — <c>section-</c> and the heading's words as a slug, made unique in order — so
/// the outline's links, a quoted passage's link and the headings agree. The body's sanitizer keeps no attribute on a
/// heading, so the ids are the outline's alone. Pure.</summary>
public static partial class Outline
{
    /// <summary>One heading: its level, its words and its id.</summary>
    public sealed record Entry(int Level, string Text, string Id);

    /// <summary>The HTML with an id on each second- and third-level heading, and those headings in order.</summary>
    public static (string Html, IReadOnlyList<Entry> Entries) Of(string html)
    {
        var entries = new List<Entry>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var anchored = Heading().Replace(html, match =>
        {
            var level = match.Groups[1].Value[0] - '0';
            var text = WebUtility.HtmlDecode(Tags().Replace(match.Groups[2].Value, "")).Trim();
            var id = "section-" + Slug.From(text, "part");
            for (var suffix = 2; !taken.Add(id); suffix++) id = "section-" + Slug.WithSuffix(Slug.From(text, "part"), suffix);
            entries.Add(new Entry(level, text, id));
            return "<h" + level.ToString(CultureInfo.InvariantCulture) + " id=\"" + id + "\">" + match.Groups[2].Value + "</h" + level.ToString(CultureInfo.InvariantCulture) + ">";
        });
        return (anchored, entries);
    }

    [GeneratedRegex(@"<h([23])>(.*?)</h\1>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Heading();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();
}
