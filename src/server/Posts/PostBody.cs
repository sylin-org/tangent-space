using System.Security.Cryptography;
using System.Text;

namespace Tangent.Posts;

/// <summary>A body as a command carries it to the writer, already judged: its Markdown, the words its sender sent, the
/// identities it mentions in the order of their first mention, and the reason it may not be written where there is
/// one. The judgement — the structure check, the one parse, the rendered size with every mention at its longest, the
/// count of identities — runs where the body is made, which is wherever its command is built, so a slow body never holds
/// the writer: a string becomes one implicitly, and what a participant sends becomes one with its typed handles written
/// as mentions (<see cref="Written"/>). The entity reads the verdict (<see cref="Post.CheckBody"/>); it never
/// parses.</summary>
public sealed class PostBody
{
    private PostBody(string text, string sent, IReadOnlyList<string> mentioned, string? refusal)
    {
        Text = text;
        Sent = sent;
        SentHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sent)));
        Mentioned = mentioned;
        Refusal = refusal;
    }

    /// <summary>The Markdown the Post keeps: the words sent, each typed handle of a participant here written as their
    /// mention.</summary>
    public string Text { get; }

    /// <summary>The Markdown as its sender sent it, which <see cref="Post.BodyLimit"/> bounds.</summary>
    public string Sent { get; }

    /// <summary>The SHA-256 of <see cref="Sent"/> in UTF-8, as lowercase hex: what recognizes the same request sent
    /// again, whoever its typed handles name by then (<see cref="Post.SentHash"/>).</summary>
    public string SentHash { get; }

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
    public static PostBody Unjudged(string text) => new(text ?? "", text ?? "", [], Post.BodyNotJudged);

    /// <summary>A body as a participant wrote it: each handle typed in its prose that names a participant here written
    /// as their mention (<see cref="Mentions.Typed"/>), then judged, so the body the writer keeps is the one judged. The
    /// rules of a glance read what was sent; a body they or the structure check refuse is judged as it was
    /// sent.</summary>
    public static async Task<PostBody> Written(string text, CancellationToken ct)
        => Glanced(text) && MarkdownHtml.Tractable(text) ? Judged(await Mentions.Typed(text, ct), text) : Of(text);

    public static PostBody Of(string text) => Judged(text ?? "", text ?? "");

    /// <summary>The verdict on <paramref name="text"/>, the Markdown the Post would keep, written from
    /// <paramref name="sent"/>.</summary>
    private static PostBody Judged(string text, string sent)
    {
        if (!Glanced(sent)) return new PostBody(text, sent, [], null);
        if (!MarkdownHtml.Tractable(text)) return new PostBody(text, sent, [], Post.BodyTooIntricate);
        MarkdownHtml.Rendered rendered;
        try
        {
            rendered = RenderedBodies.Render(text);
        }
        catch (ArgumentException)
        {
            return new PostBody(text, sent, [], Post.BodyRendersTooLong);
        }

        var mentioned = rendered.Held.Select(Mentions.Identity).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (!rendered.Fits) return new PostBody(text, sent, mentioned, Post.BodyRendersTooLong);
        return new PostBody(text, sent, mentioned, mentioned.Count > Mentions.Limit ? Post.TooManyMentions : null);
    }

    /// <summary>Whether a body as sent passes the rules a glance decides (<see cref="Post.CheckBody"/>): not empty, no
    /// NUL, at most <see cref="Post.BodyLimit"/> bytes.</summary>
    private static bool Glanced(string? text)
        => !string.IsNullOrWhiteSpace(text) && !text.Contains('\0') && Encoding.UTF8.GetByteCount(text) <= Post.BodyLimit;

    public override string ToString() => Text;
}
