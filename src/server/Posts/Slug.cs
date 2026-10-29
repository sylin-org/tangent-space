using System.Globalization;
using System.Text;

namespace Tangent.Posts;

/// <summary>The URL name of a Topic or a Tangent: lowercase letters and digits joined by single hyphens, at most
/// <see cref="Limit"/> characters. A Topic's derives from its title at creation; a Tangent's is given.</summary>
public static class Slug
{
    public const int Limit = 64;

    /// <summary>The slug a title yields: letters and digits of any script, lowercased and stripped of their marks, every
    /// other run of characters one hyphen. A title with no letter or digit yields <c>topic</c>.</summary>
    public static string From(string title)
    {
        var slug = new StringBuilder(title.Length);
        var pendingHyphen = false;
        foreach (var character in title.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                if (pendingHyphen && slug.Length > 0) slug.Append('-');
                pendingHyphen = false;
                slug.Append(char.ToLowerInvariant(character));
                if (slug.Length >= Limit) break;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var value = slug.ToString().Normalize(NormalizationForm.FormC).TrimEnd('-');
        return value.Length == 0 ? "topic" : value;
    }

    /// <summary>A slug with this suffix, cut so the whole stays within the limit.</summary>
    public static string WithSuffix(string slug, int suffix)
    {
        var tail = "-" + suffix.ToString(CultureInfo.InvariantCulture);
        var head = slug.Length + tail.Length <= Limit ? slug : slug[..(Limit - tail.Length)].TrimEnd('-');
        return head + tail;
    }

    public static bool IsValid(string? slug)
    {
        if (string.IsNullOrEmpty(slug) || slug.Length > Limit || slug[0] == '-' || slug[^1] == '-') return false;
        var previousHyphen = false;
        foreach (var character in slug)
        {
            if (character == '-')
            {
                if (previousHyphen) return false;
                previousHyphen = true;
                continue;
            }

            if (!char.IsLetterOrDigit(character) || char.IsUpper(character)) return false;
            previousHyphen = false;
        }

        return true;
    }
}
