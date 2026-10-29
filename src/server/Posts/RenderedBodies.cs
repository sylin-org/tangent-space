using Microsoft.Extensions.Caching.Memory;

namespace Tangent.Posts;

/// <summary>Each Post version's body parsed once: its HTML and plain text with every mention held (<see cref="Mentions"/>),
/// kept in a bounded cache in this process by Post Id and <see cref="Post.Version"/>, so a read parses a version at most
/// once and answers every later read — the Topic page's HTML, the door's and the Tangent page's lines — by writing the
/// held mentions with the handles it read. An entry answers only for the body it was parsed from; any other body under
/// the same key is parsed and takes its place. The cache weighs each entry by its characters and holds at most
/// <see cref="SizeLimit"/>, evicting the least recently used.</summary>
public static class RenderedBodies
{
    /// <summary>The most characters, about twice as many bytes, the cache holds.</summary>
    public const long SizeLimit = 32 * 1024 * 1024;

    private static readonly MemoryCache Cache = new(new MemoryCacheOptions { SizeLimit = SizeLimit });

    private sealed record Entry(string Body, MarkdownHtml.Rendered Rendered);

    /// <summary>The body of this Post as it stands, parsed once per version.</summary>
    public static MarkdownHtml.Rendered For(Post post) => For(post.Id, post.Version, post.Body);

    /// <summary>The body of the Post with this Id at this version, parsed once per version; <paramref name="body"/> is
    /// what the caller holds of it.</summary>
    public static MarkdownHtml.Rendered For(string postId, long version, string body)
    {
        var key = (postId, version);
        if (Cache.TryGetValue(key, out Entry? entry) && entry is not null && entry.Body == body) return entry.Rendered;
        var rendered = Render(body);
        Cache.Set(key, new Entry(body, rendered), new MemoryCacheEntryOptions { Size = body.Length + rendered.HtmlBytes });
        return rendered;
    }

    /// <summary>One parse of a body, every mention held.</summary>
    public static MarkdownHtml.Rendered Render(string body) => MarkdownHtml.Render(body, target => Mentions.Identity(target) is not null);
}
