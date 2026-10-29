using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tangent.Tangents;

/// <summary>The Tangent page as a resource: the Tangent's name, description, kind and audience, what the reader
/// <see cref="Can"/> do here, and one bounded page of its <see cref="Topics"/>, pinned first and then by last
/// activity. <see cref="Next"/> is the address of the page after this one, absent on the last; no count of
/// everything is carried. <see cref="NewTopic"/> is where a Topic is started, present only when <see cref="Can"/>
/// holds <c>Topic</c>; <see cref="SignIn"/> is where a visitor signs in when signing in is what stands between them
/// and starting one. <see cref="Place"/>, <see cref="Door"/> and <see cref="Who"/> say where the page is and who
/// reads it.</summary>
public sealed partial record TangentPage(
    string Slug,
    string Name,
    string Description,
    string Kind,
    string Audience,
    string Address,
    IReadOnlyList<string> Can,
    IReadOnlyList<TangentPage.Topic> Topics,
    string? Next,
    bool Continued,
    string? NewTopic,
    string? SignIn,
    string Place,
    string Door,
    TangentPage.Reader? Who)
{
    /// <summary>The most characters of a root Post's body a Topic's <see cref="Topic.Opening"/> carries.</summary>
    public const int OpeningLimit = 180;

    /// <summary>One Topic on the page: its root's title, slug, kind and author, the opening words of its body, its
    /// reply count, when it started and when it last gained a Post, whether it is pinned or locked, and, for a
    /// signed-in reader, whether it is <see cref="New"/> to them — its last sequence past their checkpoint for it, or
    /// no checkpoint at all — with <see cref="NewPosts"/>, how many of its Posts they have not seen.</summary>
    public sealed record Topic(
        string Id,
        string Slug,
        string Title,
        string Kind,
        string Address,
        Author Author,
        string Opening,
        int ReplyCount,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastActivityAt,
        bool Pinned,
        bool Locked,
        bool New,
        long NewPosts);

    /// <summary>Who wrote a Topic's root: the name to show, their handle, and their declared kind and avatar.</summary>
    public sealed record Author(string Id, string Handle, string Name, string Kind, string? Avatar);

    /// <summary>The signed-in participant reading the page, and where they sign out, coming back to this page.</summary>
    public sealed record Reader(string Handle, string? Avatar, string SignOut);

    /// <summary>The version the page's <c>ETag</c> carries: a digest of everything the page says, so the same page for
    /// the same reader answers the same tag, and a new Post, a moved checkpoint or a changed <c>can</c> changes it.</summary>
    public static long Version(TangentPage page)
        => BitConverter.ToInt64(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(page)), 0) & long.MaxValue;

    /// <summary>The opening words of a Markdown body as plain text on one line: link targets, images, emphasis and block
    /// markers dropped, cut at a word within <see cref="OpeningLimit"/> characters.</summary>
    public static string Opening(string body)
    {
        var text = Image().Replace(body, "");
        text = Link().Replace(text, "$1");
        text = BlockMarker().Replace(text, "");
        text = Emphasis().Replace(text, "");
        text = Whitespace().Replace(text, " ").Trim();
        if (text.Length <= OpeningLimit) return text;
        var cut = text.LastIndexOf(' ', OpeningLimit);
        return text[..(cut > OpeningLimit / 2 ? cut : OpeningLimit)].TrimEnd(',', ';', ':', '.', ' ') + "…";
    }

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?m)^\s{0,3}(?:#{1,6}\s+|>\s?|[-*+]\s+|\d+[.)]\s+|```.*$)")]
    private static partial Regex BlockMarker();

    [GeneratedRegex(@"[*_`~]+")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
