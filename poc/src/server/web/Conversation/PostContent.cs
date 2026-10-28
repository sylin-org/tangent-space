using System.Text;

namespace Tangent.Conversation;

public sealed record PostContent(string Text, DateTimeOffset CreatedAt, string? ReplyTo, string Format = "plain")
{
    public const string Plain = "plain";
    public const string Markdown = "markdown";
    public const int MaximumTextBytes = 64 * 1024;

    public static string CheckFormat(string? format)
    {
        var value = format ?? Plain;
        if (value is not Plain and not Markdown)
            throw new ArgumentException("Use plain or markdown post format.");
        return value;
    }

    public static void CheckText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaximumTextBytes || text.Contains('\0'))
            throw new ArgumentException("A post must contain 1–65536 UTF-8 bytes and no null characters.");
    }
}
