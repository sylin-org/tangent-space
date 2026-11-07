using System.Globalization;
using Koan.Data.Abstractions.Lexical;
using Koan.Data.Core;
using Koan.Data.Core.Lexical;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>The Posts whose title or body hold the words of a text, best first, among those a reader may view: every word
/// is optional, a Post holding more of them comes first, and the word the text ends in also matches as the start of a
/// word, since the text may still be being typed. Koan's lexical index does the finding, told to leave out removed Posts
/// and, for a search narrowed to one Tangent, every other Tangent, before it ranks and pages. Its ranking is read in order,
/// a batch at a time, the first through one Post past the page asked for, and each batch keeps the Posts in Tangents the
/// reader may view, each Tangent judged once as a batch names it (<see cref="ViewableTangents.Met"/>), so a page holds
/// what the reader may view in the order the index ranks it, and no page, length or score says anything of a Post the
/// reader may not view. The reading stops once the page is full and one more is kept, or at the deepest page Koan reads
/// (<see cref="LexicalQuery.MaximumOffset"/>), so a page costs the matches ranked before that one, those the reader may
/// not view among them. A partial word the store stems away finds nothing until it is whole. It never refuses: the text is trimmed and cut to <see cref="TextLimit"/>
/// characters, and text with no letter or digit finds nothing; a page holds between one and <see cref="PageLimit"/> Posts,
/// its number from one to <see cref="PageNumberLimit"/>, and a page past the last holds none. A snippet holds
/// <see cref="SnippetWords"/> words around the matched words, each between <see cref="Open"/> and <see cref="Close"/>.
/// Reads only: nothing is counted, and it takes no lock.</summary>
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

    /// <summary>Page <paramref name="page"/> (from 1) of <paramref name="size"/> Posts for <paramref name="text"/> in the
    /// Tangents <paramref name="met"/> says its reader may view, or, where <paramref name="within"/> names one, in that Tangent
    /// alone, each held within its bounds. A Post that changed Tangent or was removed while the batches were read is judged
    /// as its row reads, or left out.</summary>
    public static async Task<Answer> Run(string? text, ViewableTangents.Met met, string? within, int page = 1, int size = 20,
        CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, PageNumberLimit);
        size = Math.Clamp(size, 1, PageLimit);
        if (Words(text) is not { } words || !words.Any(char.IsLetterOrDigit)) return Answer.None;

        var batch = Math.Min(LexicalQuery.MaximumSize, page * size + 1);
        var skip = (page - 1) * size;
        var kept = new List<Hit>();
        var read = new HashSet<string>(StringComparer.Ordinal);
        for (var number = 1; (number - 1) * batch <= LexicalQuery.MaximumOffset; number++)
        {
            var matches = await Post.Lexical.Search(words, query => Readable(query.Page(number, batch).Snippet(SnippetWords, Open, Close).AsTyped(), within), ct);
            var fresh = matches.Where(match => read.Add(match.Id)).ToList();
            var rows = (await Post.Get(fresh.Select(match => match.Id), ct)).OfType<Post>()
                .Where(post => !post.Removed && (within is null || post.TangentId == within)).ToList();
            var viewable = (await met.Keep(rows, post => post.TangentId, ct)).ToDictionary(post => post.Id, StringComparer.Ordinal);
            foreach (var match in fresh)
            {
                if (!viewable.TryGetValue(match.Id, out var post)) continue;
                if (skip > 0) { skip--; continue; }
                if (kept.Count == size) return new Answer(kept, page < PageNumberLimit);
                kept.Add(new Hit(post, match.Score, match.Snippet));
            }

            if (matches.Count < batch) break;
        }

        return new Answer(kept, false);
    }

    /// <summary>The Posts the store ranks: none removed, and only those of <paramref name="within"/> where it names a
    /// Tangent.</summary>
    private static LexicalSearchQuery<Post> Readable(LexicalSearchQuery<Post> query, string? within)
        => within is null ? query.Within(post => !post.Removed) : query.Within(post => !post.Removed && post.TangentId == within);
}
