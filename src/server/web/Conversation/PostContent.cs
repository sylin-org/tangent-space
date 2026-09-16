using System.Text;

namespace Tangent.Conversation;

public sealed record PostContent(string Text, DateTimeOffset CreatedAt, SourceReference? ReplyTo)
{
    public static void CheckText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > 4096 || text.Contains('\0'))
            throw new ArgumentException("A post must contain 1–4096 UTF-8 bytes and no null characters.");
    }
}
