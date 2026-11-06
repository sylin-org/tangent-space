using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Koan.Web.OpenGraph;
using Tangent.Posts;

namespace Tangent.Tangents;

/// <summary>The Tangent page as a resource: the Tangent's name, description, kind and audience, what the reader
/// <see cref="Can"/> do here, and one bounded page of its <see cref="Topics"/>, by last activity. <see cref="Next"/> is the address of the page after this one, absent on the last; nothing is counted to
/// answer it. <see cref="NewTopic"/> is where a Topic is started, present only when <see cref="Can"/> holds
/// <c>Topic</c>; <see cref="SignIn"/> is where a visitor signs in, coming back here. <see cref="Tangents"/> are the
/// Tangents the reader may view, this one among them, with the Topics started in each. <see cref="Place"/>,
/// <see cref="Door"/> and <see cref="Who"/> say where the page is and who reads it; <see cref="Day"/> is the UTC day
/// its times are written for. <see cref="Settings"/> is where the Tangent's settings are changed, present only for a
/// reader who may change them; <see cref="NewTangent"/> is where a Tangent is created, present only for a reader who
/// may create one. <see cref="Feeds"/> are the Tangent's feeds, present only where a visitor may read it. <see cref="Leave"/> is where a
/// participant who joined a private Tangent leaves it, present only for them. <see cref="Month"/> and <see cref="Author"/>
/// say what the page is narrowed to, a month's key and the participant, absent where it is not; in an articles Tangent,
/// <see cref="Months"/> and <see cref="Authors"/> are every month its Topics started in and the authors who started the
/// most (<see cref="TangentPageController.AuthorLimit"/>), the one the list is narrowed to among them, with how many each,
/// as the Tangent keeps them, and where the list narrowed to each is.</summary>
[Render.Page(Render.Layout.Home)]
public sealed partial record TangentPage(
    string Slug,
    string Name,
    string Description,
    string Kind,
    string Audience,
    string Address,
    IReadOnlyList<string> Can,
    IReadOnlyList<Render.TopicEntry> Topics,
    string? Next,
    bool Continued,
    string? NewTopic,
    string? SignIn,
    IReadOnlyList<Render.TangentEntry> Tangents,
    string Place,
    string Door,
    TangentPage.Reader? Who,
    DateOnly Day,
    string? Settings = null,
    string? NewTangent = null,
    IReadOnlyList<Render.FeedLink>? Feeds = null,
    Render.Form? Leave = null,
    string? Month = null,
    Render.Person? Author = null,
    IReadOnlyList<Render.TopicViews.Choice>? Months = null,
    IReadOnlyList<Render.TopicViews.Choice>? Authors = null)
{
    /// <summary>This Tangent as the page lists it: its own row among <see cref="Tangents"/>, with the Topics started there.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public Render.TangentEntry Self => (Tangents.FirstOrDefault(tangent => tangent.Slug == Slug) ?? new Render.TangentEntry(Slug, Name, Kind, Address, Topics.Count))
        with { Description = Description, Address = Address };

    /// <summary>Whether the page holds every Topic of the Tangent: the first page of the whole list, with no page after it.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool Whole => !Continued && Next is null && !Narrowed;

    /// <summary>Whether the list is narrowed to a month or an author.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool Narrowed => Month is not null || Author is not null;

    [Render.Slot(Render.Slot.Head), Newtonsoft.Json.JsonIgnore]
    public Render.TangentHead Head => new(Self, Audience != "public", Whole ? Topics.Sum(topic => topic.ReplyCount) : null, Settings);

    [Render.Slot(Render.Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public Render.TopicViews Views => new(Self, !Continued && !Narrowed, Month, Author?.Id, Months ?? [], Authors ?? []);

    [Render.Slot(Render.Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public Render.AudienceNote Reach => new(Audience != "public");

    [Render.Slot(Render.Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public Render.LeaveTangent? Leaving => Leave is { } leave ? new(leave) : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.TangentTopics List => new(Self, Topics, Continued, NewTopic, SignIn, Next, To);

    /// <summary>What the list is narrowed to, in words, with how many Topics it holds where one narrowing alone names them;
    /// null for the whole list.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public Render.TangentTopics.Narrowed? To
    {
        get
        {
            if (!Narrowed) return null;
            var month = TopicList.Month.Parse(Month)?.Name;
            var words = string.Join(" ", new[] { Author is { } author ? "by " + author.Name : null, month is null ? null : "from " + month }.OfType<string>());
            var count = Month is null ? Authors?.FirstOrDefault(choice => choice.Key == Author!.Id)?.Count
                : Author is null ? Months?.FirstOrDefault(choice => choice.Key == Month)?.Count
                : null;
            return new Render.TangentTopics.Narrowed(words, count, ListAddress(Address, Month, Author?.Id));
        }
    }

    /// <summary>The address of the list of the Tangent at <paramref name="address"/>, narrowed to a month or an author
    /// where one is named, past <paramref name="after"/> where a cursor is.</summary>
    public static string ListAddress(string address, string? month, string? author, string? after = null)
    {
        var query = string.Join("&", new[]
        {
            month is null ? null : TangentPageController.Month + "=" + Uri.EscapeDataString(month),
            author is null ? null : TangentPageController.Author + "=" + Uri.EscapeDataString(author),
            after is null ? null : TangentPageController.After + "=" + Uri.EscapeDataString(after)
        }.OfType<string>());
        return query.Length == 0 ? address : address + "?" + query;
    }

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.LiveUpdates Live => new(Render.LiveUpdates.TangentPage, Slug);

    /// <summary>The frame the shell draws around the page: the place; the switcher the Tangent's crumb opens, listing the
    /// Tangents the reader may view and, for one who may, where a new one is created; and who reads, or where a visitor signs
    /// in.</summary>
    public static Render.Frame Frame(TangentPage page)
    {
        var switcher = new Render.Switcher(page.Self, page.Tangents.Select(tangent => tangent with { Current = tangent.Slug == page.Slug }).ToList(),
            page.Door + TangentsController.Route.TrimStart('/'), page.NewTangent);
        var who = page.Who is { } reader ? new Render.Who(reader.Id, reader.Name, reader.Handle, reader.Role, reader.Avatar, reader.SignOut, reader.Settings, reader.ForYou) : null;
        return new(page.Place, page.Door, [], who, page.SignIn, page.Feeds) { Switcher = switcher };
    }

    /// <summary>The most characters of a body the page carries as a Topic's <see cref="Topic.Opening"/> or a reply's
    /// <see cref="Latest.Line"/>.</summary>
    public const int OpeningLimit = 180;

    /// <summary>The words a reader reads in a minute, for an article's read time.</summary>
    public const int WordsPerMinute = 230;

    /// <summary>Someone other than a Post's author last edited it, so its words are not said to be its author's: who, and
    /// when. It travels with the Post wherever its words are read.</summary>
    public sealed record Edited(Render.Person By, DateTimeOffset At) : Render.IPart;

    /// <summary>The signed-in participant reading the page: their id, the name to show, their handle, the highest role
    /// they hold where they hold one, their avatar, where they sign out, coming back to this page, and the place's
    /// settings, for one who holds <c>ManageSpace</c>, <c>ManageStanding</c> or <c>ManageRoles</c>, and how many things
    /// are for them and new.</summary>
    public sealed record Reader(string Id, string Name, string Handle, string? Role, string? Avatar, string SignOut, string? Settings = null,
        Events.Unread.Badge? ForYou = null);

    /// <summary>The version the page's <c>ETag</c> carries: a hash of everything the page says, the day its times are
    /// written for among it, so the same page for a visitor on the same day answers the same tag, and a new Post, a
    /// changed <c>can</c> or a new day changes it.</summary>
    public static long Version(TangentPage page)
        => Render.PageVersion.Of(page);

    /// <summary>The minutes a body takes to read: its words at <see cref="WordsPerMinute"/>, at least one.</summary>
    public static int ReadTime(string body)
    {
        var words = Whitespace().Split(body.Trim()).Count(word => word.Any(char.IsLetterOrDigit));
        return Math.Max(1, (words + WordsPerMinute - 1) / WordsPerMinute);
    }

    /// <summary>Who other than its author last edited <paramref name="post"/> and when, named from
    /// <paramref name="people"/>; null where nobody did.</summary>
    public static Edited? EditedOf(Post post, IReadOnlyDictionary<string, Render.Person> people) => post.EditedBy is { } by
        ? new Edited(people.TryGetValue(by, out var person) ? person : Render.Person.Unknown(by), post.EditedAt ?? post.CreatedAt)
        : null;

    /// <summary>The opening words of a Post's body as plain text on one line, each mention of a participant among
    /// <paramref name="people"/> their name now (<see cref="Mentions.PlainText"/>), cut at a word within
    /// <see cref="OpeningLimit"/> characters.</summary>
    public static string Opening(Post post, IReadOnlyDictionary<string, Render.Person> people) => Opening(Mentions.PlainText(post, people));

    /// <summary>The opening words of plain text, cut at a word within <see cref="OpeningLimit"/> characters.</summary>
    public static string Opening(string text)
    {
        if (text.Length <= OpeningLimit) return text;
        var cut = text.LastIndexOf(' ', OpeningLimit);
        return text[..(cut > OpeningLimit / 2 ? cut : OpeningLimit)].TrimEnd(',', ';', ':', '.', ' ') + "…";
    }

    /// <summary>What a link to the Tangent previews, where a visitor may read it, which is where it has <see cref="Feeds"/>:
    /// its name, its description, its address and the place's name, all read for the page.</summary>
    public static SocialCard? Card(TangentPage page)
        => page.Feeds is null ? null : new SocialCard(page.Name, page.Description, Url: page.Address, SiteName: page.Place);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
