using System.Globalization;
using Koan.Data.Core;
using Koan.Data.Core.Lexical;

namespace Tangent.Posts;

/// <summary>The Posts whose title or body hold the words of a text, best first, among those a reader may view: every word
/// is optional, a Post holding more of them comes first, and the word the text ends in also matches as the start of a
/// word, since the text may still be being typed. Koan's lexical index does the finding, and the store is told the
/// Tangents the reader may view, or the others where they are fewer, and to leave out removed Posts, before it ranks and
/// pages, so no page, length or score says anything of a Post the reader may not view. A partial word the store stems
/// away finds nothing until it is whole. It never refuses: the text is trimmed and cut to <see cref="TextLimit"/>
/// characters, and text with no letter or digit finds nothing; a page holds between one and <see cref="PageLimit"/> Posts,
/// its number from one to <see cref="PageNumberLimit"/>, and a page past the last holds none. A snippet holds
/// <see cref="SnippetWords"/> words around the matched words, each between <see cref="Open"/> and <see cref="Close"/>.
/// Reads only: one search, asking for one Post past the page, says whether a next page holds anything; nothing is counted,
/// and it takes no lock.</summary>
public static class PostSearch
{
    public const int TextLimit = 200;
    public const int PageLimit = 50;
    public const int PageNumberLimit = 100;
    public const int SnippetWords = 24;

    /// <summary>What a snippet holds before a matched word: a private-use character, which survives reading the snippet's
    /// Markdown and which a Post's words do not otherwise hold.</summary>
    public const string Open = "";

    /// <summary>What a snippet holds after a matched word.</summary>
    public const string Close = "";

    /// <summary>One matched Post: the Post, the store's score (comparable within one answer only) and its snippet, the
    /// Markdown around the matches as the body or title holds it, in which the matched words are marked.</summary>
    public sealed record Hit(Post Post, double Score, string? Snippet);

    /// <summary>A page of hits, and whether a next page holds any.</summary>
    public sealed record Answer(IReadOnlyList<Hit> Hits, bool More)
    {
        public static readonly Answer None = new([], false);
    }

    /// <summary>The words a search reads of <paramref name="text"/>: trimmed and cut to <see cref="TextLimit"/> characters; null where
    /// nothing is left.</summary>
    public static string? Words(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > TextLimit ? trimmed[..TextLimit].TrimEnd() : trimmed;
    }

    /// <summary>The page number a search reads of <paramref name="page"/>: from one to <see cref="PageNumberLimit"/>, and one where it
    /// names no whole number.</summary>
    public static int Number(string? page)
        => int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? Math.Clamp(number, 1, PageNumberLimit) : 1;

    /// <summary>Page <paramref name="page"/> (from 1) of <paramref name="size"/> Posts for <paramref name="text"/> in
    /// <paramref name="viewable"/>, the Tangents the reader may view, and in none of <paramref name="others"/>, every other
    /// Tangent, each held within its bounds. Whatever the store answers, the page keeps only Posts in <paramref name="viewable"/>,
    /// so a Tangent that changed while the sets were read never shows; such a page can be short. No Tangent viewable reads
    /// nothing.</summary>
    public static async Task<Answer> Run(string? text, IReadOnlySet<string> viewable, IReadOnlyCollection<string> others, int page = 1, int size = 20,
        CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, PageNumberLimit);
        size = Math.Clamp(size, 1, PageLimit);
        if (Words(text) is not { } words || !words.Any(char.IsLetterOrDigit) || viewable.Count == 0) return Answer.None;

        var matches = await Post.Lexical.Search(words, query => Readable(query.Page(page, size).OneMore().Snippet(SnippetWords, Open, Close).AsTyped(),
            viewable, others), ct);
        var more = matches.Count > size;
        if (matches.Count == 0) return Answer.None;
        var shown = matches.Take(size).ToList();
        var posts = await Post.Get(shown.Select(match => match.Id), ct);
        var byId = posts.Where(post => post is not null && viewable.Contains(post.TangentId)).ToDictionary(post => post!.Id, post => post!);
        return new Answer(shown.Where(match => byId.ContainsKey(match.Id))
            .Select(match => new Hit(byId[match.Id], match.Score, match.Snippet)).ToList(), more && page < PageNumberLimit);
    }

    /// <summary>The Posts the reader may be shown: none removed, in the smaller of the two sets the caller names, to keep or
    /// to leave out, and in nothing more where none is left out.</summary>
    private static LexicalSearchQuery<Post> Readable(LexicalSearchQuery<Post> query, IReadOnlySet<string> viewable, IReadOnlyCollection<string> others)
    {
        if (others.Count == 0) return query.Within(post => !post.Removed);
        return viewable.Count <= others.Count
            ? query.Within(post => !post.Removed && viewable.Contains(post.TangentId))
            : query.Within(post => !post.Removed && !others.Contains(post.TangentId));
    }
}
