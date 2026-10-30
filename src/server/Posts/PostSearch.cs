using Koan.Data.Core;
using Koan.Data.Core.Lexical;

namespace Tangent.Posts;

/// <summary>The Posts whose title or body hold the words of a text, best first, among those a reader may view: every word
/// is optional, a Post holding more of them comes first, and the word the text ends in also matches as the start of a
/// word, since the text may still be being typed. Koan's lexical index does the finding, and the store is told the
/// Tangents the reader may view, or the others where they are fewer, and to leave out removed Posts, before it ranks and
/// pages, so no page, length or score says anything of a Post the reader may not view. A partial word the store stems
/// away finds nothing until it is whole. Bounded: the text holds at most <see cref="TextLimit"/> characters, a page at most
/// <see cref="PageLimit"/> Posts, the page number at most <see cref="PageNumberLimit"/>, and a snippet
/// <see cref="SnippetWords"/> words around the matched words, each between <see cref="Open"/> and <see cref="Close"/>.
/// Reads only, one read of the page and, where it is full, one of the next to say whether it holds anything; nothing is
/// counted, and it takes no lock.</summary>
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

    public const string NeedsText = "A search holds at least one letter or digit.";
    public const string TextTooLong = "A search holds at most 200 characters.";
    public const string NeedsPage = "A search page starts at 1 and ends at 100.";
    public const string NeedsSize = "A search page holds between 1 and 50 Posts.";

    /// <summary>One matched Post: the Post, the store's score (comparable within one answer only) and its snippet, the
    /// Markdown around the matches as the body or title holds it, in which the matched words are marked.</summary>
    public sealed record Hit(Post Post, double Score, string? Snippet);

    /// <summary>A page of hits, or the reason the search was refused.</summary>
    public abstract record Answer
    {
        private Answer() { }

        /// <summary>A page of hits, and whether a next page holds any.</summary>
        public sealed record Found(IReadOnlyList<Hit> Hits, bool More) : Answer;

        public sealed record Refused(string Reason) : Answer;
    }

    /// <summary>Page <paramref name="page"/> (from 1) of <paramref name="size"/> Posts for <paramref name="text"/> in
    /// <paramref name="viewable"/>, the Tangents the reader may view, and in none of <paramref name="others"/>, every other
    /// Tangent. Whatever the store answers, the page keeps only Posts in <paramref name="viewable"/>, so a Tangent that
    /// changed while the sets were read never shows; such a page can be short. No Tangent viewable reads nothing.</summary>
    public static async Task<Answer> Run(string? text, IReadOnlySet<string> viewable, IReadOnlyCollection<string> others, int page = 1, int size = 20,
        CancellationToken ct = default)
    {
        if (page is < 1 or > PageNumberLimit) return new Answer.Refused(NeedsPage);
        if (size is < 1 or > PageLimit) return new Answer.Refused(NeedsSize);
        if (text is { Length: > TextLimit }) return new Answer.Refused(TextTooLong);
        if (text is null || !text.Any(char.IsLetterOrDigit)) return new Answer.Refused(NeedsText);
        if (viewable.Count == 0) return new Answer.Found([], false);

        var matches = await Post.Lexical.Search(text, query => Readable(query.Page(page, size).Snippet(SnippetWords, Open, Close).AsTyped(), viewable, others), ct);
        if (matches.Count == 0) return new Answer.Found([], false);
        var more = matches.Count == size && page < PageNumberLimit
            && (await Post.Lexical.Search(text, query => Readable(query.Page(page + 1, size).AsTyped(), viewable, others), ct)).Count > 0;
        var posts = await Post.Get(matches.Select(match => match.Id), ct);
        var byId = posts.Where(post => post is not null && viewable.Contains(post.TangentId)).ToDictionary(post => post!.Id, post => post!);
        return new Answer.Found(matches.Where(match => byId.ContainsKey(match.Id))
            .Select(match => new Hit(byId[match.Id], match.Score, match.Snippet)).ToList(), more);
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
