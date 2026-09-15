using System.Text.Json;

namespace TangentSpace.Conversation;

/// <summary>Keeps a complete, contiguous window around the chosen post inside a byte budget.</summary>
public static class WindowPlanner
{
    public const int ResultBudget = 16 * 1024;
    // Reserve space for references, handles and the three protected continuation strings.
    private const int EnvelopeReserve = 4096;

    public static IReadOnlyList<Message> Select(IReadOnlyList<Message> ordered, int anchorIndex, int limit,
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

    private static int Size(Message post)
        // The stored row is a conservative bound on the smaller wire representation.
        => JsonSerializer.SerializeToUtf8Bytes(post).Length + 512;
}
