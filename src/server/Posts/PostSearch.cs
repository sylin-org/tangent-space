using Koan.Data.Core;
using Koan.Data.Core.Lexical;

namespace Tangent.Posts;

/// <summary>The Posts whose title or body hold the words of a query text, best first: every word is optional and a Post
/// holding more of them comes first. Bounded: the text holds at most <see cref="TextLimit"/> characters, a page at most
/// <see cref="PageLimit"/> Posts, the page number at most <see cref="PageNumberLimit"/>, and a snippet <see cref="SnippetWords"/> words around the matched words, each wrapped in
/// <c>&lt;mark&gt;</c>. Reads only, through Koan's lexical index, and takes no lock. Whom a Post may be shown to is the
/// caller's to decide; this answers every stored Post, removed ones included.</summary>
public static class PostSearch
{
    public const int TextLimit = 200;
    public const int PageLimit = 50;
    public const int PageNumberLimit = 100;
    public const int SnippetWords = 24;

    public const string NeedsText = "A search holds at least one letter or digit.";
    public const string TextTooLong = "A search holds at most 200 characters.";
    public const string NeedsPage = "A search page starts at 1 and ends at 100.";
    public const string NeedsSize = "A search page holds between 1 and 50 Posts.";

    /// <summary>One matched Post: the Post, the store's score (comparable within one answer only) and its snippet, in
    /// which the matched words are marked.</summary>
    public sealed record Hit(Post Post, double Score, string? Snippet);

    /// <summary>A page of hits, or the reason the query was refused.</summary>
    public abstract record Answer
    {
        private Answer() { }

        public sealed record Found(IReadOnlyList<Hit> Hits) : Answer;

        public sealed record Refused(string Reason) : Answer;
    }

    /// <summary>Page <paramref name="page"/> (from 1) of <paramref name="size"/> Posts for <paramref name="text"/>.</summary>
    public static async Task<Answer> Run(string? text, int page = 1, int size = 20, CancellationToken ct = default)
    {
        if (page is < 1 or > PageNumberLimit) return new Answer.Refused(NeedsPage);
        if (size is < 1 or > PageLimit) return new Answer.Refused(NeedsSize);
        if (text is { Length: > TextLimit }) return new Answer.Refused(TextTooLong);
        if (text is null || !text.Any(char.IsLetterOrDigit)) return new Answer.Refused(NeedsText);

        var matches = await Post.Lexical.Search(text, query => query.Page(page, size).Snippet(SnippetWords), ct);
        if (matches.Count == 0) return new Answer.Found([]);
        var posts = await Post.Get(matches.Select(match => match.Id), ct);
        var byId = posts.Where(post => post is not null).ToDictionary(post => post!.Id, post => post!);
        return new Answer.Found(matches.Where(match => byId.ContainsKey(match.Id))
            .Select(match => new Hit(byId[match.Id], match.Score, match.Snippet)).ToList());
    }
}
