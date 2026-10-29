using System.Text;

namespace Tangent.Posts;

/// <summary>A body as a command carries it to the writer, already judged: its Markdown, the identities it mentions in
/// the order of their first mention, and the reason it may not be written where there is one. The judgement — the
/// structure check, the one parse, the rendered size with every mention at its longest, the count of identities — runs
/// where the body is made, which is wherever its command is built, so a slow body never holds the writer: a string
/// becomes one implicitly. The entity reads the verdict (<see cref="Post.CheckBody"/>); it never parses.</summary>
public sealed class PostBody
{
    private PostBody(string text, IReadOnlyList<string> mentioned, string? refusal)
    {
        Text = text;
        Mentioned = mentioned;
        Refusal = refusal;
    }

    /// <summary>The Markdown as the sender wrote it.</summary>
    public string Text { get; }

    /// <summary>The DIDs the body mentions, each once, in the order of their first mention.</summary>
    public IReadOnlyList<string> Mentioned { get; }

    /// <summary>Why the body may not be written, by what its parse found; null when nothing did. The rules a glance
    /// decides — empty, a NUL, too many bytes — are <see cref="Post.CheckBody"/>'s, and a body they refuse is not
    /// parsed.</summary>
    public string? Refusal { get; }

    public static implicit operator PostBody(string text) => Of(text);

    public static PostBody Of(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\0') || Encoding.UTF8.GetByteCount(text) > Post.BodyLimit)
            return new PostBody(text ?? "", [], null);
        if (!MarkdownHtml.Tractable(text)) return new PostBody(text, [], Post.BodyTooIntricate);
        MarkdownHtml.Rendered rendered;
        try
        {
            rendered = RenderedBodies.Render(text);
        }
        catch (ArgumentException)
        {
            return new PostBody(text, [], Post.BodyRendersTooLong);
        }

        var mentioned = rendered.Held.Select(Mentions.Identity).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (!rendered.Fits) return new PostBody(text, mentioned, Post.BodyRendersTooLong);
        return new PostBody(text, mentioned, mentioned.Count > Mentions.Limit ? Post.TooManyMentions : null);
    }

    public override string ToString() => Text;
}
