using System.Globalization;

namespace Tangent.Render;

/// <summary>How the parts say times and counts, the same wherever one stands: a time as a list shows it, an exact time for its
/// attribute, a count with its noun, words joined as a sentence joins them, and text cut at a word.</summary>
public static class Wording
{
    /// <summary>A time as a list shows it: the time of day on the page's day, the month and day in its year, the full date
    /// otherwise; in UTC, which the <c>datetime</c> beside it states.</summary>
    public static string When(DateTimeOffset at, DateOnly today)
    {
        var utc = at.ToUniversalTime();
        if (DateOnly.FromDateTime(utc.UtcDateTime) == today) return utc.ToString("h:mm tt", CultureInfo.InvariantCulture);
        return utc.Year == today.Year
            ? utc.ToString("MMM d", CultureInfo.InvariantCulture)
            : utc.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>A time as a <c>datetime</c> attribute states it, in UTC.</summary>
    public static string Iso(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>When something ends, as a sentence says it, in UTC.</summary>
    public static string Until(DateTimeOffset at) => at.ToUniversalTime().ToString("MMM d, h:mm tt 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>A time in full, for its title, in UTC.</summary>
    public static string Full(DateTimeOffset at) => at.ToUniversalTime().ToString("MMM d, yyyy, h:mm tt 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>A count with its noun; none in words.</summary>
    public static string Count(int count, string one, string many, string none)
        => count == 0 ? none : count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);

    /// <summary>A count with its noun, "at least" before it where it is a lower bound.</summary>
    public static string Count(int count, bool atLeast, string one, string many)
        => (atLeast ? "at least " : "") + count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 && !atLeast ? one : many);

    /// <summary>Words joined as a sentence joins them, the last after <paramref name="last"/>: "hour, day or week".</summary>
    public static string Joined(IReadOnlyList<string> words, string last = "and")
        => words.Count < 2 ? string.Concat(words) : string.Join(", ", words.Take(words.Count - 1)) + " " + last + " " + words[^1];

    /// <summary>Text cut at the last word within <paramref name="limit"/> characters, with an ellipsis after it.</summary>
    public static string Clip(string text, int limit)
    {
        if (text.Length <= limit) return text;
        var cut = text.LastIndexOf(' ', limit);
        return text[..(cut > limit / 2 ? cut : limit)].TrimEnd(' ', ',', ';', ':', '.', '…') + "…";
    }
}
