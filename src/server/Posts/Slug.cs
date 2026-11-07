using System.Globalization;
using System.Text;
using Koan.Data.Abstractions;
using Koan.Data.Core;

namespace Tangent.Posts;

/// <summary>The URL name of a Topic or a Tangent: lowercase letters and digits joined by single hyphens, at most
/// <see cref="Limit"/> characters. A Topic's derives from its title at creation, a Tangent's from its name; neither changes.</summary>
public static class Slug
{
    public const int Limit = 64;

    /// <summary>How many numbered suffixes are tried before a Topic's own Id becomes the suffix.</summary>
    public const int SuffixBound = 50;

    /// <summary>The segments under a Tangent's address that are its own, never a Topic's slug: where a Topic is started,
    /// the Tangent's settings, where an image is brought in, where a writer's suggestions are read, where a participant
    /// joins it and leaves it, and where they quiet it.</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal) { "new", "settings", "media", "suggest", "join", "leave", "quiet" };

    /// <summary>The slug a title or a name yields: letters and digits of any script, lowercased and stripped of their
    /// marks, every other run of characters one hyphen. A title with no letter or digit yields <paramref name="otherwise"/>.</summary>
    public static string From(string title, string otherwise = "topic")
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
        return value.Length == 0 ? otherwise : value;
    }

    /// <summary>A slug with this number as its suffix, cut so the whole stays within the limit.</summary>
    public static string WithSuffix(string slug, int suffix) => WithSuffix(slug, suffix.ToString(CultureInfo.InvariantCulture));

    /// <summary>A slug with this suffix, cut so the whole stays within the limit.</summary>
    public static string WithSuffix(string slug, string suffix)
    {
        var tail = "-" + suffix;
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

    /// <summary><paramref name="slug"/> for a Topic whose root is <paramref name="id"/>, or the first numbered suffix of it
    /// that no Topic in <paramref name="tangentId"/> holds; a slug a Tangent's own pages hold (<see cref="Reserved"/>) takes the
    /// suffix too. Each try is one counted query. Past <see cref="SuffixBound"/>, the Post's own Id is the suffix, and the whole
    /// still stays within the limit.</summary>
    public static async Task<string> Unique(string tangentId, string slug, string id, CancellationToken ct)
    {
        for (var suffix = 1; suffix <= SuffixBound; suffix++)
        {
            if (suffix == 1 && Reserved.Contains(slug)) continue;
            var candidate = suffix == 1 ? slug : WithSuffix(slug, suffix);
            if (await Post.Count.Where(post => post.TangentId == tangentId && post.Slug == candidate, CountStrategy.Exact, ct) == 0) return candidate;
        }

        return WithSuffix(slug, id);
    }
}
