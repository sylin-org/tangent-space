using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Tangent.Render;

/// <summary>A <see cref="Feed"/> as an Atom document (RFC 4287). Each entry's HTML is its escaped content. A character XML
/// cannot hold, which a title or a body may, is left out.</summary>
public static class AtomFeed
{
    public const string MediaType = "application/atom+xml";

    public const string Extension = ".atom";

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    public static string Of(Feed feed)
    {
        var address = feed.Address + Extension;
        var document = new XElement(Atom + "feed",
            Text("id", address),
            Text("title", feed.Title),
            feed.Description.Length > 0 ? Text("subtitle", feed.Description) : null,
            Text("updated", Stamp(feed.Updated)),
            Link("self", MediaType, address),
            Link("alternate", "text/html", feed.Home),
            feed.Entries.Select(Entry));
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + document.ToString(SaveOptions.DisableFormatting) + "\n";
    }

    private static XElement Entry(Feed.Entry entry) => new(Atom + "entry",
        Text("id", entry.Address),
        Text("title", entry.Title),
        Link("alternate", "text/html", entry.Address),
        new XElement(Atom + "author",
            Text("name", entry.Author),
            entry.AuthorAddress is { } author ? Text("uri", author) : null),
        Text("published", Stamp(entry.Published)),
        Text("updated", Stamp(entry.Updated)),
        new XElement(Atom + "content", new XAttribute("type", "html"), Holdable(entry.Html)));

    private static XElement Text(string name, string value) => new(Atom + name, Holdable(value));

    private static XElement Link(string rel, string type, string address)
        => new(Atom + "link", new XAttribute("rel", rel), new XAttribute("type", type), new XAttribute("href", address));

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary><paramref name="text"/> without the characters XML cannot hold.</summary>
    private static string Holdable(string text)
    {
        var kept = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (XmlConvert.IsXmlChar(text[index])) kept.Append(text[index]);
            else if (index + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[index + 1], text[index])) kept.Append(text[index]).Append(text[++index]);
        }

        return kept.ToString();
    }
}
