namespace Tangent.Posts;

/// <summary>Keeps a window of a Topic complete and contiguous around the Post it is chosen for, inside a byte budget as
/// well as a count. The anchor always fits, or the read fails; the window then grows one Post at a time, alternating
/// older and newer, and a side whose next Post would overflow the budget is closed for the rest of the plan, so the
/// window never skips a Post to fit a later one. Each Post costs what it weighs plus <see cref="PostOverhead"/>, and
/// <see cref="EnvelopeReserve"/> is kept for everything around the Posts: the Topic, the authors, the links.</summary>
public static class WindowPlanner
{
    /// <summary>The most bytes one answer carries.</summary>
    public const int ResultBudget = 1024 * 1024;

    /// <summary>The most bytes one answer carries for an article, whose root is a published piece read beside its
    /// replies.</summary>
    public const int ArticleResultBudget = 2 * ResultBudget;

    /// <summary>The bytes kept for what the answer holds besides its Posts.</summary>
    public const int EnvelopeReserve = 32 * 1024;

    /// <summary>The bytes each Post costs beyond its own, for its author, its <c>can</c> and its links.</summary>
    public const int PostOverhead = 1024;

    /// <summary>The most Posts one window holds.</summary>
    public const int Limit = 25;

    /// <summary>The budget of an answer about a Topic of this kind.</summary>
    public static int Budget(PostKind kind) => kind == PostKind.Article ? ArticleResultBudget : ResultBudget;

    /// <summary>The contiguous run of <paramref name="ordered"/> around <paramref name="anchorIndex"/>, at most
    /// <paramref name="limit"/> Posts, whose costs with the reserve stay within <paramref name="budget"/>.</summary>
    public static IReadOnlyList<Post> Select(IReadOnlyList<Post> ordered, int anchorIndex, int limit, int budget, Func<Post, int> weight)
    {
        if (limit is < 1 or > Limit) throw new ArgumentOutOfRangeException(nameof(limit));
        if (ordered.Count == 0) return [];
        if (anchorIndex < 0 || anchorIndex >= ordered.Count) throw new ArgumentOutOfRangeException(nameof(anchorIndex));
        var left = anchorIndex;
        var right = anchorIndex;
        var bytes = EnvelopeReserve + Cost(ordered[anchorIndex]);
        if (bytes > budget) throw new InvalidDataException("The anchor Post exceeds the window's budget.");
        var tryLeft = true;
        var leftClosed = false;
        var rightClosed = false;
        while (right - left + 1 < limit)
        {
            var canLeft = !leftClosed && left > 0;
            var canRight = !rightClosed && right + 1 < ordered.Count;
            if (!canLeft && !canRight) break;
            var takeLeft = canLeft && (!canRight || tryLeft);
            var candidate = takeLeft ? left - 1 : right + 1;
            var cost = Cost(ordered[candidate]);
            if (bytes + cost > budget)
            {
                if (takeLeft) leftClosed = true;
                else rightClosed = true;
            }
            else
            {
                bytes += cost;
                if (takeLeft) left--;
                else right++;
            }

            tryLeft = !takeLeft;
        }

        return ordered.Skip(left).Take(right - left + 1).ToArray();

        int Cost(Post post) => weight(post) + PostOverhead;
    }
}
