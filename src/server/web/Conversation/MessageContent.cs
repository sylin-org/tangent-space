using System.Text;

namespace TangentSpace.Conversation;

public sealed record MessageContent(string Text, DateTimeOffset CreatedAt, SourceReference? ReplyTo)
{
    public static void CheckText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > 4096 || text.Contains('\0'))
            throw new ArgumentException("A message must contain 1–4096 UTF-8 bytes and no null characters.");
    }
}
