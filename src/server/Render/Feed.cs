using System.Text;
using System.Text.RegularExpressions;

namespace Tangent.Render;

/// <summary>A feed as a resource, which <see cref="AtomFeed"/> and <see cref="JsonFeed"/> write out: its title and
/// description, the page it follows, its <see cref="Address"/> without the extension each format adds, and its
/// <see cref="Entries"/>, newest first, each with its HTML already rendered and made safe. It knows nothing of what its
/// entries are.</summary>
public sealed partial record Feed(string Title, string Description, string Home, string Address, IReadOnlyList<Feed.Entry> Entries)
{
    /// <summary>The most entries a feed holds.</summary>
    public const int Limit = 20;

    /// <summary>The most bytes of HTML, with <see cref="EntryWeight"/> for each entry, a feed holds.</summary>
    public const int Budget = 1024 * 1024;

    /// <summary>What an entry weighs beyond its HTML: its title, its author and its addresses.</summary>
    public const int EntryWeight = 1024;

    /// <summary>One entry: where it reads, its title, its HTML, who wrote it and where they are, when it was published and
    /// when its words last changed.</summary>
    public sealed record Entry(string Address, string Title, string Html, string Author, string? AuthorAddress, DateTimeOffset Published, DateTimeOffset Updated);

    /// <summary>When the feed last changed: its newest entry's change, so an unchanged feed writes out the same bytes. A
    /// feed with no entries says the epoch.</summary>
    public DateTimeOffset Updated => Entries.Count == 0 ? DateTimeOffset.UnixEpoch : Entries.Max(entry => entry.Updated);

    /// <summary>The window of <paramref name="newestFirst"/> a feed holds: the newest entry always, then each older one
    /// until <see cref="Limit"/> or <see cref="Budget"/>, never skipping an entry to fit a later one.</summary>
    public static IReadOnlyList<Entry> Window(IEnumerable<Entry> newestFirst)
    {
        var window = new List<Entry>();
        long bytes = 0;
        foreach (var entry in newestFirst)
        {
            bytes += Encoding.UTF8.GetByteCount(entry.Html) + EntryWeight;
            if (window.Count == Limit || (window.Count > 0 && bytes > Budget)) break;
            window.Add(entry);
        }

        return window;
    }

    /// <summary><paramref name="html"/> with each link and image a page would resolve against itself made absolute, since
    /// a feed reader reads it elsewhere: a root-relative target from <paramref name="origin"/>, a fragment from
    /// <paramref name="address"/>. Only tags are touched: rendered HTML holds a <c>&lt;</c> nowhere else, and a quotation mark
    /// nowhere inside an attribute's value.</summary>
    public static string Absolute(string html, string origin, string address)
        => LinkTag().Replace(html, tag => RelativeTarget().Replace(tag.Value, target => target.Value == "/" ? origin + "/" : address + "#"));

    /// <summary><paramref name="html"/> with each link and image that starts at a host's root made absolute from
    /// <paramref name="origin"/>, since a page standing alone has no host. Only tags are touched, as in
    /// <see cref="Absolute"/>.</summary>
    public static string Rooted(string html, string origin)
        => LinkTag().Replace(html, tag => RootTarget().Replace(tag.Value, _ => origin + "/"));

    [GeneratedRegex("<(?:a|img)\\s(?:[^>\"]|\"[^\"]*\")*>", RegexOptions.CultureInvariant)]
    private static partial Regex LinkTag();

    [GeneratedRegex("(?<=\\s(?:href|src)=\")[/#]", RegexOptions.CultureInvariant)]
    private static partial Regex RelativeTarget();

    [GeneratedRegex("(?<=\\s(?:href|src)=\")/", RegexOptions.CultureInvariant)]
    private static partial Regex RootTarget();
}

/// <summary>A feed as a page advertises it: its title, its media type and its address.</summary>
public sealed record FeedLink(string Title, string Type, string Address)
{
    /// <summary>The two formats of the feed at <paramref name="address"/>, its address without an extension.</summary>
    public static IReadOnlyList<FeedLink> Pair(string title, string address) =>
    [
        new(title + " (Atom)", AtomFeed.MediaType, address + AtomFeed.Extension),
        new(title + " (JSON Feed)", JsonFeed.MediaType, address + JsonFeed.Extension)
    ];
}
