using System.Text;

namespace Tangent.Render;

/// <summary>A sentence the page says, with code between backticks, words to stress between double asterisks and links as
/// <c>[words](address)</c>; the server writes it, and what it quotes it escapes (<see cref="Link"/>, <see cref="Strong"/>,
/// <see cref="Coded"/>), so a name holding any of those marks reads as written.</summary>
public sealed record Sentence(string Text) : IPart
{
    /// <summary>Words read as written, whatever marks they hold.</summary>
    public static string Plain(string words)
    {
        var escaped = new StringBuilder(words.Length);
        foreach (var unit in words)
        {
            if (unit is '\\' or '`' or '*' or '[' or ']' or '(' or ')') escaped.Append('\\');
            escaped.Append(unit);
        }

        return escaped.ToString();
    }

    /// <summary>A link to <paramref name="address"/> reading <paramref name="words"/>; its words are read as a sentence of their
    /// own, so they are escaped twice.</summary>
    public static string Link(string words, string address) => "[" + Plain(Plain(words)) + "](" + Plain(address) + ")";

    /// <summary>A link to <paramref name="address"/> reading <paramref name="code"/> in the mono face.</summary>
    public static string CodeLink(string code, string address) => "[" + Plain(Coded(code)) + "](" + Plain(address) + ")";

    /// <summary>Words to stress.</summary>
    public static string Strong(string words) => "**" + Plain(words) + "**";

    /// <summary>Code, in the mono face.</summary>
    public static string Coded(string code) => "`" + code.Replace("`", "'") + "`";

    /// <summary>The sentence as its pieces: plain words, code, stressed words and links, in order.</summary>
    public IReadOnlyList<Piece> Pieces()
    {
        var pieces = new List<Piece>();
        var words = new StringBuilder();
        var text = Text;
        void Flush()
        {
            if (words.Length == 0) return;
            pieces.Add(new Piece(Kind.Words, words.ToString()));
            words.Clear();
        }

        for (var at = 0; at < text.Length; at++)
        {
            var unit = text[at];
            if (unit == '\\' && at + 1 < text.Length)
            {
                words.Append(text[++at]);
            }
            else if (unit == '`' && text.IndexOf('`', at + 1) is var end and > 0)
            {
                Flush();
                pieces.Add(new Piece(Kind.Code, text[(at + 1)..end]));
                at = end;
            }
            else if (unit == '*' && at + 1 < text.Length && text[at + 1] == '*' && Until(text, at + 2, "**") is { } stressed)
            {
                Flush();
                pieces.Add(new Piece(Kind.Strong, stressed.Words));
                at = stressed.End + 1;
            }
            else if (unit == '[' && Until(text, at + 1, "](") is { } label && Until(text, label.End + 2, ")") is { } target)
            {
                Flush();
                pieces.Add(new Piece(Kind.Link, label.Words, target.Words));
                at = target.End;
            }
            else
            {
                words.Append(unit);
            }
        }

        Flush();
        return pieces;
    }

    /// <summary>The words from <paramref name="from"/> to the first unescaped <paramref name="close"/>, unescaped, and where the
    /// close begins; null where none closes them.</summary>
    private static (string Words, int End)? Until(string text, int from, string close)
    {
        var words = new StringBuilder();
        for (var at = from; at < text.Length; at++)
        {
            if (text[at] == '\\' && at + 1 < text.Length)
            {
                words.Append(text[++at]);
                continue;
            }

            if (string.CompareOrdinal(text, at, close, 0, close.Length) == 0) return (words.ToString(), at);
            words.Append(text[at]);
        }

        return null;
    }

    public enum Kind
    {
        Words,
        Code,
        Strong,
        Link
    }

    /// <summary>One piece of a sentence: its kind, its words, and where a link goes.</summary>
    public sealed record Piece(Kind Kind, string Words, string? Address = null);
}
