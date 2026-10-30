using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tangent.Posts;

namespace Tangent.Tangents;

/// <summary>The Tangent page as a resource: the Tangent's name, description, kind and audience, what the reader
/// <see cref="Can"/> do here, and one bounded page of its <see cref="Topics"/>, pinned first and then by last
/// activity. <see cref="Next"/> is the address of the page after this one, absent on the last; nothing is counted to
/// answer it. <see cref="NewTopic"/> is where a Topic is started, present only when <see cref="Can"/> holds
/// <c>Topic</c>; <see cref="Watching"/> is the form that sets or clears a signed-in reader's watch flag on the
/// Tangent; <see cref="SignIn"/> is where a visitor signs in, coming back here. <see cref="Tangents"/> are the
/// Tangents the reader may view, this one among them, with the Topics started in each. <see cref="Place"/>,
/// <see cref="Door"/> and <see cref="Who"/> say where the page is and who reads it; <see cref="Day"/> is the UTC day
/// its times are written for. <see cref="Settings"/> is where the Tangent's settings are changed, present only for a
/// reader who may change them; <see cref="NewTangent"/> is where a Tangent is created, present only for a reader who
/// may create one.</summary>
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
    Events.Watching? Watching,
    string? SignIn,
    IReadOnlyList<TangentPage.Listed> Tangents,
    string Place,
    string Door,
    TangentPage.Reader? Who,
    DateOnly Day,
    string? Settings = null,
    string? NewTangent = null)
{
    /// <summary>The most characters of a body the page carries as a Topic's <see cref="Topic.Opening"/> or a reply's
    /// <see cref="Latest.Line"/>.</summary>
    public const int OpeningLimit = 180;

    /// <summary>The words a reader reads in a minute, for an article's read time.</summary>
    public const int WordsPerMinute = 230;

    /// <summary>One Topic on the page: its root's title, slug, kind and author, the opening words of its body, the
    /// minutes it takes to read, its reply count, when it started and when it last gained a Post, whether it is pinned
    /// or locked, its <see cref="Latest"/> reply and its most recent <see cref="People"/>, and, for a signed-in reader,
    /// whether it is <see cref="New"/> to them — its last sequence past their checkpoint for it, or no checkpoint at
    /// all — with <see cref="NewPosts"/>, how many of its Posts they have not seen.</summary>
    public sealed record Topic(
        string Id,
        string Slug,
        string Title,
        string Kind,
        string Address,
        Author Author,
        string Opening,
        int ReadMinutes,
        int ReplyCount,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastActivityAt,
        bool Pinned,
        bool Locked,
        Latest? Latest,
        IReadOnlyList<Author> People,
        bool New,
        long NewPosts);

    /// <summary>A Topic's latest reply: its author, its first words, when it was written, and whether it is new to the
    /// reader.</summary>
    public sealed record Latest(string Id, Author Author, string Line, DateTimeOffset At, bool New);

    /// <summary>A participant as the page shows them: the name to show, their handle, their kind and avatar, and, where
    /// the page links their name, their profile's address.</summary>
    public sealed record Author(string Id, string Handle, string Name, string Kind, string? Avatar, string? Profile = null);

    /// <summary>A Tangent the reader may view: its slug, name, kind, address and how many Topics were started there.</summary>
    public sealed record Listed(string Slug, string Name, string Kind, string Address, int TopicCount);

    /// <summary>The signed-in participant reading the page: their id, the name to show, their handle, the highest role
    /// they hold where they hold one, their avatar, and where they sign out, coming back to this page.</summary>
    public sealed record Reader(string Id, string Name, string Handle, string? Role, string? Avatar, string SignOut);

    /// <summary>The version the page's <c>ETag</c> carries: a hash of everything the page says, the day its times are
    /// written for among it, so the same page for a visitor on the same day answers the same tag, and a new Post, a
    /// changed <c>can</c> or a new day changes it. A signed-in reader's page carries their watch form with a new
    /// antiforgery token every time, so its version never repeats and it is never answered as unchanged.</summary>
    public static long Version(TangentPage page)
        => BitConverter.ToInt64(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(page)), 0) & long.MaxValue;

    /// <summary>The minutes a body takes to read: its words at <see cref="WordsPerMinute"/>, at least one.</summary>
    public static int ReadTime(string body)
    {
        var words = Whitespace().Split(body.Trim()).Count(word => word.Any(char.IsLetterOrDigit));
        return Math.Max(1, (words + WordsPerMinute - 1) / WordsPerMinute);
    }

    /// <summary>The opening words of a Post's body as plain text on one line, each mention of a participant among
    /// <paramref name="people"/> their current handle (<see cref="Mentions.PlainText"/>), cut at a word within
    /// <see cref="OpeningLimit"/> characters.</summary>
    public static string Opening(Post post, IReadOnlyDictionary<string, Author> people)
    {
        var text = Mentions.PlainText(post, people);
        if (text.Length <= OpeningLimit) return text;
        var cut = text.LastIndexOf(' ', OpeningLimit);
        return text[..(cut > OpeningLimit / 2 ? cut : OpeningLimit)].TrimEnd(',', ';', ':', '.', ' ') + "…";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
