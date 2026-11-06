using Tangent.Render;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>A search of what the place has said, one resource both ways: <see cref="Text"/>, the words searched for, null
/// before anyone has; one bounded page of <see cref="Hits"/>, best first, only ever Posts the reader may view;
/// and <see cref="Next"/> and <see cref="Previous"/>, the addresses of the pages either side, each absent where there is none.
/// <see cref="Place"/>,
/// <see cref="Door"/>, <see cref="SignIn"/> and <see cref="Who"/> say where the page is and who reads it, and
/// <see cref="Day"/> is the UTC day its times are written for.</summary>
[Render.Page(Render.Layout.Panel)]
public sealed record SearchPage(
    string Place,
    string Door,
    string Address,
    string? Text,
    IReadOnlyList<SearchPage.Hit> Hits,
    string? Next,
    string? Previous,
    string? SignIn,
    Who? Who,
    DateOnly Day)
{
    /// <summary>Where a search answers under the base URL.</summary>
    public const string Path = "/search";

    /// <summary>One Post found: a Topic's root (<c>topic</c> or <c>article</c>) or a <c>reply</c>, the title of its Topic, the
    /// Tangent it is in, its address, who wrote it and when, who other than them last edited it where someone did, and its
    /// <see cref="Snippet"/>: its words around the matches in plain text, as runs, the matched words marked, or its first
    /// words, unmarked, where the title is what matched. <see cref="TangentSlug"/> and <see cref="TopicSlug"/> are what its
    /// address names, which a tool takes.</summary>
    public sealed record Hit(string Id, string Kind, string Topic, string Tangent, string Address, Render.Person Author, DateTimeOffset At,
        IReadOnlyList<Run> Snippet, TangentPage.Edited? Edited = null, string TangentSlug = "", string TopicSlug = "") : Render.IPart;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new(Text is { } text ? "Search: " + text : "Search", "In", Place, Door);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.SearchHits Found => new(Hits,
        Text is null ? "Type the words you’re looking for in the box at the top of the page." : "Nothing you can read here holds those words.", Previous, Next);

    /// <summary>The frame the shell draws around the page: the search as its one crumb, who reads or where a visitor signs in,
    /// and the words, which the masthead's box holds.</summary>
    public static Render.Frame Frame(SearchPage page)
        => new(page.Place, page.Door, [new Render.Crumb("Search")], page.Who, page.SignIn, Searched: page.Text);

    /// <summary>A stretch of a snippet's words, marked where they are what matched. A run is words and never markup, so a
    /// page or an agent shows it safely whatever a Post's body holds.</summary>
    public sealed record Run(string Text, bool Marked);

    /// <summary>The runs of a snippet's plain text, split at the marks <see cref="PostSearch"/> asked the store for.</summary>
    public static IReadOnlyList<Run> Runs(string marked)
    {
        var runs = new List<Run>();
        var at = 0;
        while (at < marked.Length)
        {
            var open = marked.IndexOf(PostSearch.Open, at, StringComparison.Ordinal);
            if (open < 0) break;
            var close = marked.IndexOf(PostSearch.Close, open, StringComparison.Ordinal);
            if (close < 0) break;
            if (open > at) runs.Add(new Run(marked[at..open], false));
            if (close > open + PostSearch.Open.Length) runs.Add(new Run(marked[(open + PostSearch.Open.Length)..close], true));
            at = close + PostSearch.Close.Length;
        }

        var rest = marked[at..].Replace(PostSearch.Open, "", StringComparison.Ordinal).Replace(PostSearch.Close, "", StringComparison.Ordinal);
        if (rest.Length > 0) runs.Add(new Run(rest, false));
        return runs;
    }
}
