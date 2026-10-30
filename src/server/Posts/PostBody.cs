using System.Text;

namespace Tangent.Posts;

/// <summary>A body as a command carries it to the writer, already judged: its Markdown, the identities it mentions in
/// the order of their first mention, and the reason it may not be written where there is one. The judgement — the
/// structure check, the one parse, the rendered size with every mention at its longest, the count of identities — runs
/// where the body is made, which is wherever its command is built, so a slow body never holds the writer: a string
/// becomes one implicitly, and what a participant sends becomes one with its typed handles written as mentions
/// (<see cref="Written"/>). The entity reads the verdict (<see cref="Post.CheckBody"/>); it never parses.</summary>
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

    /// <summary>A body left unjudged, for a command the writer's gate will refuse before it reads the body: a replay of
    /// a Post already written still answers, and a body that reaches the entity regardless is refused with
    /// <see cref="Post.BodyNotJudged"/>, never written.</summary>
    public static PostBody Unjudged(string text) => new(text ?? "", [], Post.BodyNotJudged);

    /// <summary>A body as a participant wrote it, judged (<see cref="Of"/>) once each handle typed in its prose that
    /// names a participant here is written as their mention (<see cref="Mentions.Typed"/>), so the body the writer keeps
    /// is the one judged. A body the rules of a glance or the structure check refuse is judged as it was sent.</summary>
    public static async Task<PostBody> Written(string text, CancellationToken ct)
        => Of(Glanced(text) && MarkdownHtml.Tractable(text) ? await Mentions.Typed(text, ct) : text);

    public static PostBody Of(string text)
    {
        if (!Glanced(text)) return new PostBody(text ?? "", [], null);
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

    /// <summary>Whether a body passes the rules a glance decides (<see cref="Post.CheckBody"/>): not empty, no NUL, at
    /// most <see cref="Post.BodyLimit"/> bytes.</summary>
    private static bool Glanced(string? text)
        => !string.IsNullOrWhiteSpace(text) && !text.Contains('\0') && Encoding.UTF8.GetByteCount(text) <= Post.BodyLimit;

    public override string ToString() => Text;
}
