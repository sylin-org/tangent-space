using System.ComponentModel;
using System.Security.Claims;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>What quieting a Tangent sends, as a form or one JSON object.</summary>
public sealed record QuietInput([Description(Holds.Quiet)] bool? Quiet = null);

/// <summary>The Tangent page, one resource both ways: the Tangent, its <c>can</c>, and one bounded page of its Topics,
/// whole or narrowed to a month or an author (<see cref="TopicList.Narrowing"/>), the whole list's first page led by the
/// Topic its moderators pinned (<see cref="TopicList.Pinned"/>), with what is new to the reader, a pinned Topic and a closed one marked,
/// and in an articles Tangent every month it narrows to and the <see cref="AuthorLimit"/> authors who started the most,
/// from the counts its row keeps, left to the formatter as HTML, a fragment or JSON. It needs
/// <see cref="Bit.View"/>, which the Doorway judges before anything else (<see cref="Access.Doorway"/>), so a reader without it
/// receives its gate, and one for whom the Tangent is absent what an address with no Tangent receives; a participant outside a
/// members-only Tangent they may join or ask to join reads its card instead, and nothing of what it holds
/// (<see cref="TangentOutside"/>). Every read is bounded
/// and keyed: the Tangent and capability's rows, which the Doorway read, the pinned Topic's root by Id on a page of the whole list, one read of Topics, their latest replies by Id, the people on the page and those the rail names, the reader's place mark and their marks on
/// those Topics, which say what is new to them (<see cref="Unread"/>), the first <see cref="SwitcherLimit"/>
/// Tangents the reader may view for the switcher (<see cref="ViewableTangents"/>), and, for a signed-in reader, what they
/// hold in the place, which says whether the switcher offers a new Tangent, and whether they quieted it, from the row the
/// Doorway read, with the form that quiets it or lets it count again (<see cref="Quiet"/>).</summary>
public sealed class TangentPageController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    /// <summary>The query parameter that narrows the list to a month, by its key (<see cref="TopicList.Month.Key"/>).</summary>
    public const string Month = "month";

    /// <summary>The query parameter that narrows the list to the Topics a participant started, by their id.</summary>
    public const string Author = "author";

    /// <summary>The segment under a Tangent's address where a Topic is started; <see cref="Slug.Reserved"/> keeps it
    /// from every Topic.</summary>
    public const string NewTopicSegment = "new";

    /// <summary>The most Tangents the switcher lists; the rest are a link away (<see cref="TangentsController"/>).</summary>
    public const int SwitcherLimit = 50;

    /// <summary>The most authors an articles Tangent's rail offers, those who started the most there; the list narrows to
    /// anyone else at their own address all the same (<see cref="Author"/>).</summary>
    public const int AuthorLimit = 10;

    /// <summary>The segment under a Tangent's address where a participant quiets it or lets it count again;
    /// <see cref="Slug.Reserved"/> keeps it from every Topic.</summary>
    public const string QuietSegment = "quiet";

    /// <summary>The field that says whether to quiet.</summary>
    public const string QuietField = "quiet";

    /// <summary>Whether the participant quieted the Tangent, as a JSON answer says it: its slug, its address and
    /// <c>quiet</c>.</summary>
    public sealed record Quieted(string Slug, string Address, bool Quiet);

    [HttpGet("/t/{tangent}")]
    [HttpHead("/t/{tangent}")]
    [Tool(Name = "read_tangent")]
    [Needs(Bit.View, Outside = true)]
    [Description("Read a Tangent: its Topics a page at a time, narrowed to a month or an author where you name one, the one its moderators pinned first on the first page of them all, what is new to you, pinned or closed marked, and a Topic whose root was removed saying who removed it; an articles Tangent lists every month it narrows to and the authors who started the most, with how many each, and narrows to any author by their id; quiet says whether you quieted it. Then call read_topic on a Topic's slug, start_topic to begin one, or quiet_tangent. A members-only Tangent you may join or ask to join answers its card instead, with joining; then call join_tangent.")]
    public async Task<IActionResult> Get([Description(Holds.Tangent)] string tangent, [FromQuery(Name = After), Description(Holds.After)] string? after,
        [FromQuery(Name = Month), Description(Holds.Month)] string? month, [FromQuery(Name = Author), Description(Holds.Author)] string? author,
        CancellationToken ct)
    {
        var here = Here.Of(HttpContext);
        var personId = here.PersonId;
        var row = here.Tangent!;
        var capabilities = here.Capabilities;
        if (here.Outside is { } outside) return await Outside(here, row, outside, ct);

        TopicList.Cursor? cursor = null;
        if (after is not null && (cursor = TopicList.Cursor.Parse(after)) is null)
            return new Problem(StatusCodes.Status400BadRequest,
                $"The {After} parameter is the cursor a page of this Tangent names as its next.") { Title = "Not a page of this Tangent" }.ToResult();

        TopicList.Month? within = null;
        if (month is not null && (within = TopicList.Month.Parse(month)) is null)
            return new Problem(StatusCodes.Status400BadRequest,
                $"The {Month} parameter is a month as its year and number, such as 2026-10.") { Title = "Not a month" }.ToResult();
        var by = string.IsNullOrWhiteSpace(author) ? null : author.Trim();

        // The whole list leaves its pinned Topic out of every page and leads its first with it; a narrowed one reads in its
        // own order, marking the pinned Topic where it falls.
        var narrowing = new TopicList.Narrowing(within, by);
        var pinned = narrowing.Narrowed ? null : await TopicList.Pinned(row, ct);
        var page = await TopicList.Read(row.Id, cursor, ct, to: narrowing, leaving: pinned?.Id);
        IReadOnlyList<Post> topics = pinned is not null && cursor is null ? [pinned, .. page.Topics] : page.Topics;
        var pinnedId = narrowing.Narrowed ? row.PinnedId : pinned?.Id;
        var reader = here.Rows.Participant;
        var latest = await LatestReplies(topics, ct);
        var articles = row.DefaultKind == PostKind.Article;
        var offered = articles ? Offered(row, by) : [];
        IEnumerable<string> named = offered.Select(author => author.Key);
        var people = await People(topics, latest.Values, ct, by is null ? named : named.Append(by));
        var unread = reader is null ? null : await Unread.Of(reader, ct);
        var marks = unread is null ? new Dictionary<string, long>() : await unread.Marks(topics.Select(topic => topic.Id), ct);
        var space = here.Rows.Space;
        var audience = space.Audience == Audience.Private || await Overwrites.AudienceOf(row.Id, ct) == Audience.Private
            ? Audience.Private
            : Audience.Public;

        var baseAddress = BaseAddress();
        var address = Address(row.Id);
        var back = Uri.EscapeDataString(baseUrl.PathBase + "/t/" + row.Id);
        // A signed-in reader's page is never stored, so a browser's Back after sign-out redisplays nothing of it.
        if (reader is not null) Response.Headers.CacheControl = "no-cache, no-store";
        var place = reader is null ? null : await Capabilities.Load(personId, null, null, ct);
        var creates = place is not null && place.Holds(Bit.ManageSpace);
        return Ok(new TangentPage(
            row.Id,
            row.Name,
            row.Description,
            Word(row.DefaultKind),
            audience.ToString().ToLowerInvariant(),
            address,
            capabilities.Can,
            topics.Select(topic => Topic(topic, address, people, latest, unread, marks) with { Pinned = topic.Id == pinnedId && !topic.Removed }).ToList(),
            page.Next is { } next ? TangentPage.ListAddress(address, within?.Key, by, next.ToString()) : null,
            cursor is not null,
            capabilities.Holds(Bit.Topic) ? address + "/" + NewTopicSegment : null,
            reader is null ? baseAddress + SignInController.Path + "?return=" + back : null,
            await Viewable(personId, row, ct),
            space.Name,
            baseAddress + "/",
            reader is null ? null : Render.Who.Of(Render.Person.Of(reader, baseAddress), Role(capabilities),
                baseAddress + AuthConstants.Routes.Logout + "?return=" + back, place is null ? null : TangentSpaceSettingsController.Entry(place, baseAddress),
                await unread!.BadgeNow(baseAddress + "/", ct)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            capabilities.Holds(Bit.ManageTangent) ? address + "/" + TangentWrites.SettingsSegment : null,
            creates ? baseAddress + NewTangentController.Route : null,
            audience == Audience.Public ? Feeds.TangentLinks(baseAddress, row.Id, row.Name, space.Name) : null,
            reader is not null && Inside.In(await Inside.Own(row.Id, reader.Id, ct))
                ? Render.Form.Posting(address + "/" + JoinController.LeaveSegment)
                : null,
            within?.Key,
            by is null ? null : Person(people, by),
            articles ? Months(row, address) : null,
            articles ? Authors(offered, address, people) : null)
        {
            Quiet = reader?.Quiet.Contains(row.Id),
            Quieting = reader is null ? null : QuietForm(address, reader.Quiet.Contains(row.Id))
        });
    }

    [HttpPost("/t/{tangent}/" + QuietSegment)]
    [Tool(Name = "quiet_tangent", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.View, Participant = true)]
    [Description("Quiet a Tangent, so what is new in it stops counting in arrive's news and on your door, while replies to you, mentions of you and the Topics you follow there still do. With quiet false, it counts again.")]
    public async Task<IActionResult> Quiet([FromRoute, Description(Holds.Tangent)] string tangent, QuietInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var row = Here.Of(HttpContext).Tangent!;
        var outcome = await new QuietTangent(Here.Of(HttpContext).PersonId!, row.Id, input.Quiet ?? true).Send(ct);
        if (outcome is not Outcome<Participant>.Done { Result: var participant }) return Answers.NotDone(HttpContext, outcome);
        var address = Address(row.Id);
        if (PostWrites.WantsJson(Request)) return Ok(new Quieted(row.Id, address, participant.Quiet.Contains(row.Id)));
        Response.Headers.Location = address;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    /// <summary>The form that quiets the Tangent at <paramref name="address"/>, or, where it is <paramref name="quiet"/>, lets
    /// it count again.</summary>
    public static Render.Form QuietForm(string address, bool quiet)
        => Render.Form.Posting(address + "/" + QuietSegment, new Render.Form.Field(QuietField, Holds.Quiet, quiet ? "false" : "true", Render.Form.Input.Hidden))
            with { Tool = "quiet-tangent", Submit = quiet ? "Stop quieting" : "Quiet this Tangent", Quiet = true };

    /// <summary>Every month the Tangent's Topics started in, newest first, with how many, as its row keeps them.</summary>
    private static IReadOnlyList<Render.TopicViews.Choice> Months(Tangent row, string address) => row.Months
        .Select(month => (Month: TopicList.Month.Parse(month.Key), Count: month.Value))
        .Where(month => month.Month is not null)
        .OrderByDescending(month => month.Month!.Start)
        .Select(month => new Render.TopicViews.Choice(month.Month!.Key, month.Month.Name, month.Count, TangentPage.ListAddress(address, month.Month.Key, null)))
        .ToList();

    /// <summary>The authors the rail offers, with how many Topics each started, as the Tangent's row keeps them: the
    /// <see cref="AuthorLimit"/> who started the most, by id where as many started the same, and the one the list is
    /// narrowed to where they started one and are not among them.</summary>
    private static IReadOnlyList<KeyValuePair<string, int>> Offered(Tangent row, string? by)
    {
        var most = row.Authors.OrderByDescending(author => author.Value).ThenBy(author => author.Key, StringComparer.Ordinal).Take(AuthorLimit).ToList();
        if (by is not null && row.Authors.TryGetValue(by, out var count) && most.All(author => author.Key != by)) most.Add(new(by, count));
        return most;
    }

    /// <summary>The authors the rail offers as its choices, most Topics first, then by name.</summary>
    private static IReadOnlyList<Render.TopicViews.Choice> Authors(IEnumerable<KeyValuePair<string, int>> offered, string address,
        IReadOnlyDictionary<string, Render.Person> people)
        => offered
            .Select(author => new Render.TopicViews.Choice(author.Key, Person(people, author.Key).Name, author.Value, TangentPage.ListAddress(address, null, author.Key)))
            .OrderByDescending(author => author.Count).ThenBy(author => author.Name, StringComparer.CurrentCulture).ThenBy(author => author.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>The members-only Tangent as a participant outside it reads it: its card, with the way in, in the place's
    /// frame. Never stored, since it names its reader.</summary>
    private async Task<IActionResult> Outside(Here here, Tangent row, Outsider outside, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-cache, no-store";
        var baseAddress = BaseAddress();
        var back = baseUrl.PathBase + "/t/" + row.Id;
        return Ok(new TangentOutside(TangentsController.Outside(row, baseAddress, outside.Asked), here.Rows.Space.Name, baseAddress + "/",
            await PostWrites.Reader(here.PersonId, here.Capabilities, baseAddress, back, ct)));
    }

    /// <summary>One Topic as a list shows it, what is new in it marked for a signed-in reader by the one rule
    /// (<see cref="Unread.InTopic"/>), and none for a visitor, who removed its root, where someone did, and whether it is
    /// closed. Whether it is pinned is the list's to say.</summary>
    public static Render.TopicEntry Topic(Post root, string tangentAddress, IReadOnlyDictionary<string, Render.Person> people,
        IReadOnlyDictionary<string, Post> latest, Unread? unread, IReadOnlyDictionary<string, long> marks)
    {
        long? mark = marks.TryGetValue(root.Id, out var read) ? read : null;
        var (isNew, newPosts) = unread?.InTopic(root, mark) ?? (false, 0);
        var address = tangentAddress + "/" + Uri.EscapeDataString(root.Slug ?? root.Id);
        Render.TopicEntry.Reply? reply = null;
        if (root.LatestReplyId is { } replyId && latest.TryGetValue(replyId, out var post))
            reply = new Render.TopicEntry.Reply(post.Id, Person(people, post.AuthorId), TangentPage.Opening(post, people), post.CreatedAt,
                unread is not null && unread.New(post, mark), TangentPage.EditedOf(post, people));
        return new Render.TopicEntry(
            root.Id,
            root.Slug ?? root.Id,
            root.Title ?? "",
            Word(root.Kind),
            address,
            Person(people, root.AuthorId),
            TangentPage.Opening(root, people),
            TangentPage.ReadTime(root.Body),
            root.ReplyCount,
            root.CreatedAt,
            root.LastActivityAt,
            reply,
            root.RecentAuthorIds.Take(Post.RecentAuthorLimit).Select(id => Person(people, id)).ToList(),
            isNew,
            newPosts,
            TangentPage.EditedOf(root, people),
            root.CoverId is { } cover ? tangentAddress[..tangentAddress.LastIndexOf("/t/", StringComparison.Ordinal)] + Media.Reference(cover) : null,
            root.Removed ? new TopicPage.Removal(Person(people, root.RemovedBy ?? root.AuthorId), root.RemovedAt ?? root.CreatedAt) : null)
        {
            Closed = root.Closed
        };
    }

    private static Render.Person Person(IReadOnlyDictionary<string, Render.Person> people, string id)
        => people.TryGetValue(id, out var person) ? person : Render.Person.Unknown(id);

    /// <summary>The latest reply of each Topic on the page that has one, by its Id, in one keyed read.</summary>
    public static async Task<IReadOnlyDictionary<string, Post>> LatestReplies(IReadOnlyList<Post> topics, CancellationToken ct)
    {
        var ids = topics.Select(topic => topic.LatestReplyId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Post>();
        return (await Post.Get(ids, ct)).OfType<Post>().ToDictionary(post => post.Id, StringComparer.Ordinal);
    }

    /// <summary>Everyone the page shows (the Topics' authors, their recent authors, the authors of their latest
    /// replies, whom those openings and replies mention, who else edited them, who removed a root, and the participants
    /// <paramref name="also"/> names) in one keyed read, by participant Id.</summary>
    public static async Task<IReadOnlyDictionary<string, Render.Person>> People(IReadOnlyList<Post> topics, IEnumerable<Post> latest,
        CancellationToken ct, IEnumerable<string>? also = null)
    {
        var ids = (also ?? []).Concat(topics.SelectMany(topic => topic.RecentAuthorIds.Take(Post.RecentAuthorLimit).Append(topic.AuthorId)))
            .Concat(latest.Select(post => post.AuthorId))
            .Concat(topics.Concat(latest).SelectMany(post => post.MentionedIds))
            .Concat(topics.Concat(latest).Select(post => post.EditedBy).OfType<string>())
            .Concat(topics.Where(topic => topic.Removed).Select(topic => topic.RemovedBy).OfType<string>()).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Render.Person>();
        var rows = await Participant.Get(ids, ct);
        return rows.OfType<Participant>().ToDictionary(row => row.Id, row => Render.Person.Of(row), StringComparer.Ordinal);
    }


    /// <summary>The highest seeded role the reader holds, in words; null for none.</summary>
    private static string? Role(Capabilities capabilities) => Roles.Word(capabilities.RoleIds);

    /// <summary>The first <see cref="SwitcherLimit"/> Tangents the reader may view, this one always among them.</summary>
    private async Task<IReadOnlyList<Render.TangentEntry>> Viewable(string? personId, Tangent current, CancellationToken ct)
    {
        var listed = (await ViewableTangents.Read(personId, ViewableTangents.Views, null, SwitcherLimit, ct)).Tangents.ToList();
        if (listed.All(row => row.Id != current.Id)) listed.Add(current);
        return listed.Select(row => TangentEntry.Of(row.Id == current.Id ? current : row, Address(row.Id), BaseAddress() + "/")).ToList();
    }

    private string Address(string slug) => BaseAddress() + "/t/" + Uri.EscapeDataString(slug);

    private static string Word(PostKind kind) => kind.ToString().ToLowerInvariant();

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
