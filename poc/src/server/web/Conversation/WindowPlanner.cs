using System.Text.Json;

namespace Tangent.Conversation;

/// <summary>Keeps a complete, contiguous window around the chosen post inside a byte budget.</summary>
public static class WindowPlanner
{
    public const int ResultBudget = 1024 * 1024;
    // Article reads carry one canonical opening Post alongside a normal bounded window.
    public const int ArticleResultBudget = 2 * ResultBudget;
    // Reserve space for references, handles and the three protected continuation strings.
    private const int EnvelopeReserve = 32 * 1024;

    public static void CheckContent(string text, string format)
    {
        var content = new PostContent(text, default, null, format);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { content.Text, content.Format,
            Html = PostMarkup.Html(content) }).Length;
        if (bytes > ResultBudget - 64 * 1024)
            throw new ArgumentException("This post exceeds the supported reading window size.");
    }

    public static T CheckArticleResponse<T>(T response)
    {
        if (JsonSerializer.SerializeToUtf8Bytes(response).Length > ArticleResultBudget)
            throw new InvalidDataException("The article exceeds the supported reading window size.");
        return response;
    }

    public static IReadOnlyList<Post> Select(IReadOnlyList<Post> ordered, int anchorIndex, int limit,
        int resultBudget = ResultBudget)
    {
        if (limit is < 1 or > 25) throw new ArgumentOutOfRangeException(nameof(limit));
        if (ordered.Count == 0) return [];
        if (anchorIndex < 0 || anchorIndex >= ordered.Count) throw new ArgumentOutOfRangeException(nameof(anchorIndex));
        var left = anchorIndex;
        var right = anchorIndex;
        var bytes = EnvelopeReserve + Size(ordered[anchorIndex]);
        if (bytes > resultBudget) throw new InvalidDataException("The anchor post exceeds the supported window budget.");
        var tryLeft = true;
        var leftBlocked = false;
        var rightBlocked = false;
        while (right - left + 1 < limit)
        {
            var canLeft = !leftBlocked && left > 0;
            var canRight = !rightBlocked && right + 1 < ordered.Count;
            if (!canLeft && !canRight) break;
            var takeLeft = canLeft && (!canRight || tryLeft);
            var candidate = takeLeft ? left - 1 : right + 1;
            var size = Size(ordered[candidate]);
            if (bytes + size > resultBudget)
            {
                if (takeLeft) leftBlocked = true; else rightBlocked = true;
            }
            else
            {
                bytes += size;
                if (takeLeft) left--; else right++;
            }
            tryLeft = !takeLeft;
        }
        return ordered.Skip(left).Take(right - left + 1).ToArray();
    }

    private static int Size(Post post)
        => JsonSerializer.SerializeToUtf8Bytes(PostReadProjection.Of(post)).Length + 1024;
}
