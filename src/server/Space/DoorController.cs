using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Events;
using Tangent.Offers;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Tangents;

namespace Tangent.Space;

/// <summary>The door, one resource both ways, at the base URL. Its <c>GET</c> writes nothing and is a composition of
/// named reads, each bounded and keyed: what is for a signed-in reader and new to them (<see cref="Unread.ForYouNow"/>);
/// what is new in each Tangent and the front page, both from one window of the log (<see cref="LogWindow"/>): the Topics
/// that gained a Post in it, newest first, articles <see cref="Door.Featured"/> and the rest <see cref="Door.Alive"/>,
/// and their writers <see cref="Door.Around"/>; for a visitor and a reader with nothing new, a shelf of each of the first
/// Tangents' latest Topics (<see cref="Shelves"/>); for the place's operator, the open Offer of an address
/// (<see cref="AddressOffers.Open"/>), shown first; for whoever holds <see cref="Bit.ManageStanding"/>, how many
/// applications wait, linked to where they are decided; one seen form whenever the door has news; and the poll link.
/// Everything new is new by the one rule (<see cref="Unread"/>). The door's <c>gate</c> is why the reader may not write
/// in the place, which an applicant's door and a timed-out participant's, with when it ends, say in one sentence. A
/// reader without <see cref="Bit.View"/> on the place receives its gate, worded for reading. With <see cref="Before"/>, a
/// signed-in reader's door is paged back (<see cref="DoorPast"/>), and a visitor's ignores it.</summary>
public sealed class DoorController(KoanWebBaseUrl baseUrl, AddressOffers offers) : ControllerBase
{
    /// <summary>The most Tangents the door lists: the first page of those the reader may view
    /// (<see cref="ViewableTangents"/>), with the moderators' Tangent after them for whoever may view it, and, where more
    /// follow or the reader may post and so join a members-only one, a link to all of them (<see cref="TangentsController"/>).</summary>
    public const int TangentLimit = TangentPageController.SwitcherLimit;

    public const int FeaturedLimit = 3;

    public const int AliveLimit = 6;

    public const int AroundLimit = 8;

    /// <summary>The most Tangents that get a shelf, and the most Topics a shelf holds.</summary>
    public const int ShelfLimit = 6;

    public const int ShelfTopicLimit = 3;

    /// <summary>The query parameter that pages the door back: what reached the reader before that sequence of the log and is
    /// no longer new (<see cref="DoorPast"/>).</summary>
    public const string Before = "before";

    [HttpGet("/")]
    [HttpHead("/")]
    [LlmsText.Pointer]
    [Tool(Name = "arrive")]
    [Description("Call this first when you come back, to see who you are here, what is new since you were here and what you may do, with links. Then call read_tangent on a Tangent's slug, read_topic or read_post on what a new Post's address names, /t/{tangent}/{topic}/{post}, and seen with news.through once you have read it. news.following lists the Topics you follow with replies new to you. With before, a sequence such as the one news.older names, the things for you before it that are no longer new, newest first, in older.items, and older.next for the page before them.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> Get([FromQuery(Name = Before), Description(Holds.Before)] string? before, CancellationToken ct)
    {
        var personId = PersonId(User);
        var place = Here.Of(HttpContext).Around;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct)
            ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        var appearance = HttpContext.Items[HtmlFormatter.Previewed] as Appearance ?? space.Appearance;
        var description = HttpContext.Items[HtmlFormatter.PreviewedDescription] as string ?? space.Description;
        var reader = personId is null ? null : await Participant.Get(personId, ct);
        var unread = reader is null ? null : await Unread.Of(reader, ct);
        var baseAddress = BaseAddress();
        if (unread is not null && before is not null)
            return long.TryParse(before, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) && sequence >= 1
                ? await Past(unread, sequence, space, place, baseAddress, ct)
                : new Problem(StatusCodes.Status400BadRequest, $"The {Before} parameter is the sequence the door names as its older.")
                    { Title = "Not a page of the door" }.ToResult();

        var forYou = unread is null ? null : await unread.ForYouNow(ct);
        var window = await LogWindow.Newest(ct);
        var page = await ViewableTangents.Read(personId, ViewableTangents.Views, null, TangentLimit, ct);
        var tangentIds = window.Concat(forYou?.Rows ?? []).Select(e => e.TangentId).OfType<string>()
            .Concat(forYou?.Following.Select(topic => topic.Root.TangentId) ?? [])
            .Concat(space.ModeratorsTangentId is { } moderators ? [moderators] : [])
            .Distinct(StringComparer.Ordinal).Where(id => !page.Each.ContainsKey(id)).ToList();
        var each = new Dictionary<string, Capabilities>(page.Each, StringComparer.Ordinal);
        foreach (var (id, on) in await Capabilities.LoadEach(personId, tangentIds, ct)) each[id] = on;
        bool Viewable(string? tangentId) => tangentId is not null && each.TryGetValue(tangentId, out var on) && on.Exists;
        // A row of a Tangent's slug from before that Tangent took it is a deleted Tangent's, which reaches no one.
        bool Reached(Event e) => Viewable(e.TangentId) && each[e.TangentId!].Owns(e.Seq);
        var tangents = await BySlug(page.Tangents, tangentIds.Where(Viewable).ToList(), ct);
        // The moderators' Tangent comes after every other, so past the first page of a long list; whoever may view it
        // finds it on the door whatever the place holds.
        var listed = space.ModeratorsTangentId is { } kept && page.Tangents.All(tangent => tangent.Id != kept) && tangents.TryGetValue(kept, out var reports)
            ? [.. page.Tangents, reports]
            : page.Tangents;

        var created = window.Where(e => e.Kind == EventKind.PostCreated && Reached(e) && e.PostId is not null && e.TopicId is not null).ToList();
        var posts = await Posts(created, forYou?.Posts, ct);
        var marks = unread is null ? new Dictionary<string, long>() : await unread.Marks(created.Select(e => e.TopicId!), ct);
        var fresh = unread is null ? [] : Fresh(created, unread, posts, marks);
        var (featured, alive, writers) = Front(created, posts);

        var now = DateTimeOffset.UtcNow;
        var forYouGroups = forYou is null || reader is null ? null : ForYouOf(forYou, reader.Id);
        var people = await People(
            baseAddress,
            new[] { space.ProprietorId, reader?.Id }
                .Concat(forYouGroups?.People() ?? [])
                .Concat(fresh.Where(e => Article(e, posts)).Select(e => e.ActorId))
                .Concat(writers.Take(AroundLimit))
                .Concat(featured.Concat(alive).SelectMany(root => root.RecentAuthorIds.Take(Post.RecentAuthorLimit).Append(root.AuthorId)))
                .Concat((forYouGroups?.Shown() ?? []).Concat(fresh.Where(e => Article(e, posts)).Take(Unread.CountLimit)).Select(e => posts[e.PostId!])
                    .Concat(featured).Concat(alive).SelectMany(post => post.MentionedIds.Append(post.EditedBy))),
            ct);

        Door.Since? news = null;
        var through = 0L;
        if (unread is not null && forYou is not null && forYouGroups is not null)
        {
            // Read through the newest event in a Tangent the reader may view, their own included, and what is for them; an
            // event in a Tangent they may not view moves nothing they can see.
            var newestViewable = window.Where(Reached).Select(e => e.Seq).DefaultIfEmpty(0).Max();
            through = new[] { unread.Floor, newestViewable, forYou.Through }.Max();
            news = News(unread, forYou, forYouGroups, fresh, window, posts, tangents, people, await RoleNames(forYouGroups.Roles, ct), through, baseAddress)
                with { Let = await Tangents.Asking.Decided(forYouGroups.Let, people, baseAddress, ct) };
            // A door that carries a form is never stored, so a browser's Back after sign-out redisplays nothing of it; any
            // other door of a signed-in reader is theirs alone to cache.
            if (!news.Empty && through > unread.Floor) news = news with { Seen = SeenController.DoorForm(baseAddress, through) };
            Response.Headers.CacheControl = news.Seen is null ? "private" : "no-cache, no-store";
        }

        var offered = reader is not null && reader.Id == space.OperatorId ? await offers.Offered(space, baseAddress, ct) : null;
        if (offered is not null) Response.Headers.CacheControl = "no-cache, no-store";

        // Whoever decides applications reads how many wait, seen or not, and reaches them in one click.
        var waiting = reader is not null && place.Holds(Bit.ManageStanding)
            ? await Applications.Waiting(0, long.MaxValue, Unread.CountLimit + 1, ct)
            : [];

        var asking = reader is null ? [] : await Tangents.Asking.For(reader.Id, baseAddress, ct);

        var steps = FirstSteps.For(place, space, listed, page.Next is not null, reader, reader is null ? "" : Person(people, reader.Id).Name, baseAddress);
        if (steps is not null) Response.Headers.CacheControl = "no-cache, no-store";
        var shelves = steps is null && (news is null || news.Empty)
            ? await Shelves(page.Tangents.Where(tangent => tangent.Id != space.ModeratorsTangentId).ToList(),
                featured.Concat(alive).Select(root => root.Id).ToHashSet(StringComparer.Ordinal), people, baseAddress, ct)
            : null;

        var freshIn = fresh.GroupBy(e => e.TopicId!, StringComparer.Ordinal).ToDictionary(topic => topic.Key, topic => topic.Count(), StringComparer.Ordinal);
        var newIn = news?.PerTangent.ToDictionary(tangent => tangent.Slug, tangent => tangent.Topics + tangent.Replies, StringComparer.Ordinal);
        var door = baseAddress + "/";
        var back = Uri.EscapeDataString(baseUrl.PathBase + "/");
        return Ok(new Door(
            space.Name,
            door,
            space.ProprietorId is { } proprietor ? Person(people, proprietor) : null,
            place.Can,
            listed
                .Select(tangent => TangentEntry.Of(tangent, Address(tangent.Id), door) with { New = newIn?.GetValueOrDefault(tangent.Id) ?? 0 })
                .ToList(),
            page.Next is not null,
            featured.Select(root => Shown(root, tangents, people, freshIn)).ToList(),
            alive.Select(root => Shown(root, tangents, people, freshIn)).ToList(),
            reader is null ? baseAddress + SignInController.Path + "?return=" + back : null,
            reader is null ? null : Who.Of(Person(people, reader.Id), Roles.Word(place.RoleIds), baseAddress + AuthConstants.Routes.Logout + "?return=" + back,
                TangentSpaceSettingsController.Entry(place, baseAddress),
                forYou is null ? null : new Unread.Badge(forYou.Count, forYou.AtLeast, door, forYou.Through, forYou.Applications)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            Resume(baseAddress, through > 0 ? through : window.Count > 0 ? window[0].Seq : 0),
            place.Holds(Bit.ManageSpace) ? baseAddress + NewTangentController.Route : null)
        {
            Around = new Door.Crowd(writers.Count, window.Count >= LogWindow.Size, writers.Take(AroundLimit).Select(id => Person(people, id)).ToList()),
            News = news,
            Description = description,
            DescriptionHtml = PlaceWords.Html(PlaceWords.DescriptionPart, description, baseAddress + "/"),
            Intro = PlaceWords.Html(Appearance.IntroPart, appearance.Intro, baseAddress + "/"),
            Image = appearance.ImageAt(baseAddress + "/"),
            Feeds = space.Audience == Audience.Public ? Feeds.ArticlesLinks(baseAddress, space.Name) : null,
            Offer = offered,
            FirstSteps = steps,
            Shelves = shelves is { Count: > 0 } ? shelves : null,
            Applications = waiting.Count == 0
                ? null
                : new Door.Waiting(waiting.Count, waiting.Count > Unread.CountLimit, ApplicationsController.WaitingAddress(baseAddress)),
            Asks = asking.Count == 0 ? null : asking,
            // The list of every Tangent is also where whoever may post finds the members-only ones they may join or ask to join.
            AllTangents = page.Next is not null || (reader is not null && place.Holds(Bit.Post)) ? baseAddress + TangentsController.Route : null,
            Gate = place.Gate(Bit.Post)?.Word,
            Until = reader is not null && reader.StandingAt(now) == Standing.TimedOut ? reader.StandingUntil : null
        });
    }

    /// <summary>The shelves of the first <see cref="ShelfLimit"/> Tangents that hold a Topic those <paramref name="above"/>
    /// does not: each Tangent's pinned Topic, then its latest, <see cref="ShelfTopicLimit"/> in all past those, in its list's own
    /// sort (<see cref="TopicList.For"/>), at most one keyed read of the pinned root and one bounded read of the topic list of
    /// each Tangent the door lists, so a Tangent whose every Topic the front page shows gets no shelf, then one keyed read of
    /// their latest replies and one of the people they name whom <paramref name="known"/> does not hold.</summary>
    private static async Task<List<Door.Shelf>> Shelves(IReadOnlyList<Tangents.Tangent> tangents, IReadOnlySet<string> above,
        IReadOnlyDictionary<string, Render.Person> known, string baseAddress, CancellationToken ct)
    {
        var read = new List<(Tangents.Tangent Tangent, List<Post> Topics, string? Pinned)>();
        foreach (var tangent in tangents.Where(tangent => tangent.TopicCount > 0))
        {
            if (read.Count == ShelfLimit) break;
            var size = Math.Min(TopicList.PageSize, ShelfTopicLimit + above.Count);
            var pinned = await TopicList.Pinned(tangent, ct);
            var newest = (await TopicList.Read(tangent.Id, null, ct, size, sort: TopicList.For(tangent), leaving: pinned?.Id)).Topics;
            var topics = (pinned is null ? newest : newest.Prepend(pinned)).Where(root => !root.Removed && !above.Contains(root.Id))
                .Take(ShelfTopicLimit).ToList();
            if (topics.Count > 0) read.Add((tangent, topics, pinned?.Id));
        }

        var roots = read.SelectMany(shelf => shelf.Topics).ToList();
        var latest = await TangentPageController.LatestReplies(roots, ct);
        var people = new Dictionary<string, Render.Person>(known, StringComparer.Ordinal);
        foreach (var (id, person) in await People(baseAddress, roots
                     .SelectMany(root => root.RecentAuthorIds.Take(Post.RecentAuthorLimit).Append(root.AuthorId).Concat(root.MentionedIds))
                     .Concat(latest.Values.SelectMany(reply => reply.MentionedIds.Append(reply.AuthorId).Append(reply.EditedBy)))
                     .Concat(roots.Select(root => root.EditedBy))
                     .Where(id => id is not null && !known.ContainsKey(id)), ct))
            people[id] = person;

        var marks = new Dictionary<string, long>();
        return read.Select(shelf =>
        {
            var address = PostWrites.TangentAddress(baseAddress, shelf.Tangent.Id);
            return new Door.Shelf(shelf.Tangent.Id, shelf.Tangent.Name, Word(shelf.Tangent.DefaultKind), shelf.Tangent.Description, address,
                Tile.For(shelf.Tangent, baseAddress + "/"), shelf.Tangent.TopicCount,
                shelf.Topics.Select(root => TangentPageController.Topic(root, address, people, latest, null, marks) with { Pinned = root.Id == shelf.Pinned }).ToList());
        }).ToList();
    }

    /// <summary>The Posts the window's new Topics and replies brought that are new to the reader by the one rule: in a
    /// Tangent they may view and did not quiet (<see cref="Participant.Quiet"/>), someone else's, standing in a Topic that
    /// stands, past where they were last here and past their mark on the Topic.</summary>
    private static List<Event> Fresh(IReadOnlyList<Event> created, Unread unread, IReadOnlyDictionary<string, Post> posts,
        IReadOnlyDictionary<string, long> marks)
        => created.Where(e => !unread.Reader.Quiet.Contains(e.TangentId!) && posts.TryGetValue(e.PostId!, out var post) && !post.Removed
                              && posts.TryGetValue(e.TopicId!, out var root) && !root.Removed
                              && unread.New(post, marks.TryGetValue(root.Id, out var mark) ? mark : null))
            .ToList();

    /// <summary>The front page from the window: the Topics that gained a Post in it and stand, newest first, articles
    /// featured and the rest alive, and who wrote in it, newest first.</summary>
    private static (List<Post> Featured, List<Post> Alive, List<string> Writers) Front(IReadOnlyList<Event> created, IReadOnlyDictionary<string, Post> posts)
    {
        var active = created.Select(e => e.TopicId).OfType<string>().Distinct(StringComparer.Ordinal)
            .Select(id => posts.GetValueOrDefault(id)).OfType<Post>().Where(root => !root.Removed).ToList();
        return (active.Where(root => root.Kind == PostKind.Article).Take(FeaturedLimit).ToList(),
            active.Where(root => root.Kind != PostKind.Article).Take(AliveLimit).ToList(),
            created.Select(e => e.ActorId).OfType<string>().Distinct(StringComparer.Ordinal).ToList());
    }

    /// <summary>What is for the reader, by what it is.</summary>
    private sealed record ForYouGroups(
        IReadOnlyList<Event> Replies,
        IReadOnlyList<Event> Taken,
        IReadOnlyList<Event> Mentions,
        IReadOnlyList<Event> Reports,
        IReadOnlyList<Event> Edited,
        IReadOnlyList<Event> Removed,
        Event? Admitted,
        Event? Lifted,
        Event? TimedOut,
        Event? Declined,
        IReadOnlyList<Event> Roles,
        IReadOnlyList<Event> Let)
    {
        /// <summary>Who the groups name.</summary>
        public IEnumerable<string?> People() => Replies.Concat(Taken).Concat(Mentions).Concat(Reports).Concat(Edited).Concat(Removed).Concat(Roles).Concat(Let)
            .Concat(new[] { Admitted, Lifted, TimedOut, Declined }.OfType<Event>()).Select(e => e.ActorId);

        /// <summary>The rows the door carries with a Post's first words.</summary>
        public IEnumerable<Event> Shown() => new[] { Replies, Taken, Mentions, Reports, Edited }.SelectMany(rows => rows.Take(Unread.CountLimit));
    }

    /// <summary>What is for the reader, by what it is: a reply to them, a Topic taken on a tangent from their Post, a
    /// mention of them, a report, their Post someone else edited or removed, the newest change someone else made of their
    /// standing — who admitted them, lifted their timeout or ban, timed them out or declined their application — and the
    /// roles someone gave them or took from them. A ban has no line: the place is absent to whoever it bans.</summary>
    private static ForYouGroups ForYouOf(Unread.ForYou forYou, string readerId)
    {
        var rows = forYou.Rows;
        bool Taken(Event e) => forYou.Posts.TryGetValue(e.PostId ?? "", out var post) && post is { ParentId: null, OriginId: not null };
        var decided = rows.FirstOrDefault(e => e.Kind == EventKind.StandingChanged);
        return new ForYouGroups(
            rows.Where(e => e is { Kind: EventKind.PostCreated, Report: not true } && e.TargetId == readerId && !Taken(e)).ToList(),
            rows.Where(e => e is { Kind: EventKind.PostCreated, Report: not true } && e.TargetId == readerId && Taken(e)).ToList(),
            rows.Where(e => e.Kind == EventKind.Mention).ToList(),
            rows.Where(e => e is { Kind: EventKind.PostCreated, Report: true }).ToList(),
            rows.Where(e => e.Kind == EventKind.PostEdited).ToList(),
            rows.Where(e => e.Kind == EventKind.PostRemoved).ToList(),
            decided is { Standing: Standing.Admitted, Before: not (Standing.TimedOut or Standing.Banned) } ? decided : null,
            decided is { Standing: Standing.Admitted, Before: Standing.TimedOut or Standing.Banned } ? decided : null,
            decided is { Standing: Standing.TimedOut } ? decided : null,
            decided is { Standing: Standing.Declined } ? decided : null,
            rows.Where(e => e is { Kind: EventKind.RoleChanged, Given: not null }).ToList(),
            rows.Where(e => e is { Kind: EventKind.TangentLet, Given: not null }).ToList());
    }

    /// <summary>What is new to the reader, said: what is for them, then what is new in each Tangent and the new
    /// articles, each count a lower bound where a read it came from was full. What is for the reader wins: a new article
    /// that replies to them, is taken from their Post, mentions them or is a report counts there and not among the new
    /// articles.</summary>
    private Door.Since News(Unread unread, Unread.ForYou forYou, ForYouGroups groups, IReadOnlyList<Event> fresh, IReadOnlyList<Event> window,
        IReadOnlyDictionary<string, Post> posts, IReadOnlyDictionary<string, Tangents.Tangent> tangents,
        IReadOnlyDictionary<string, Render.Person> people, IReadOnlyDictionary<string, string> roleNames, long through, string baseAddress)
    {
        var partial = LogWindow.Partial(window, unread.Floor);
        Door.Group Group(IReadOnlyList<Event> events, bool atLeast) => this.Group(events, atLeast, posts, tangents, people);
        var forReader = groups.Replies.Concat(groups.Taken).Concat(groups.Mentions).Concat(groups.Reports)
            .Select(e => e.PostId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        // A followed Topic's new Posts are counted among the Topics followed, and nowhere else in the sentence.
        var followed = forYou.Following.Select(topic => topic.Root.Id).ToHashSet(StringComparer.Ordinal);
        return new Door.Since(
            unread.Floor,
            through,
            Group(groups.Replies, forYou.AtLeast),
            Group(groups.Mentions, forYou.AtLeast),
            fresh.GroupBy(e => e.TangentId!, StringComparer.Ordinal)
                .Select(byTangent => new Door.InTangent(byTangent.Key, Name(tangents, byTangent.Key),
                    tangents.TryGetValue(byTangent.Key, out var tangent) ? Word(tangent.DefaultKind) : Word(PostKind.Topic), Address(byTangent.Key),
                    byTangent.Count(e => e.PostId == e.TopicId), byTangent.Count(e => e.PostId != e.TopicId),
                    byTangent.Count(e => !forReader.Contains(e.PostId!) && !Article(e, posts) && !followed.Contains(e.TopicId!)), partial))
                .ToList(),
            Group(fresh.Where(e => Article(e, posts) && !forReader.Contains(e.PostId!)).ToList(), partial),
            null,
            Group(groups.Taken, forYou.AtLeast),
            groups.Admitted is { } admitted ? new Door.Decision(Person(people, admitted.ActorId!), admitted.At) : null,
            groups.Lifted is { } lifted ? new Door.Decision(Person(people, lifted.ActorId!), lifted.At, Participants.People.Word(lifted.Before!.Value)) : null,
            groups.Roles.Select(e => new Door.RoleChange(Person(people, e.ActorId!), e.At, Roles.Word(e.Role!), e.Given!.Value,
                roleNames.GetValueOrDefault(e.Role!) ?? Roles.Word(e.Role!))).ToList(),
            Group(groups.Edited, forYou.AtLeast),
            Group(groups.Removed, forYou.AtLeast),
            Group(groups.Reports, forYou.AtLeast),
            groups.TimedOut is { } timedOut ? new Door.Decision(Person(people, timedOut.ActorId!), timedOut.At, Until: unread.Reader.StandingUntil) : null,
            groups.Declined is { } declined ? new Door.Decision(Person(people, declined.ActorId!), declined.At) : null,
            forYou.Waiting > 0 ? new Door.Waiting(forYou.Waiting, forYou.AtLeast, ApplicationsController.WaitingAddress(baseAddress)) : null,
            Following: forYou.Following.Select(topic => new Door.Followed(topic.Root.Id, topic.Root.Title ?? "", Name(tangents, topic.Root.TangentId),
                TopicAddress(topic.Root), topic.New)).ToList(),
            Older: OlderAddress(baseAddress, through + 1));
    }

    /// <summary>The door's address of what reached the reader before the log's sequence <paramref name="before"/> and is
    /// no longer new.</summary>
    private static string OlderAddress(string baseAddress, long before)
        => baseAddress + "/?" + Before + "=" + before.ToString(CultureInfo.InvariantCulture);

    /// <summary>The door paged back for a signed-in reader (<see cref="DoorPast"/>): one page of what reached them before
    /// <paramref name="before"/> and is no longer new (<see cref="Unread.Earlier"/>), with what is for them now for the
    /// masthead's count, the first page of the Tangents they may view and the moderators' after them for whoever may view
    /// it, as the door lists them, and one keyed read each of the other Tangents and of the people its rows name. Theirs
    /// alone to cache.</summary>
    private async Task<IActionResult> Past(Unread unread, long before, TangentSpace space, Capabilities place, string baseAddress, CancellationToken ct)
    {
        var reader = unread.Reader;
        var forYou = await unread.ForYouNow(ct);
        var past = await unread.Earlier(before, ct);
        var page = await ViewableTangents.Read(reader.Id, ViewableTangents.Views, null, TangentLimit, ct);
        var viewable = past.Rows.Select(e => e.TangentId).OfType<string>().ToList();
        var moderators = space.ModeratorsTangentId is { } kept && (await Capabilities.LoadEach(reader.Id, [kept], ct)).Any(on => on.Value.Exists) ? kept : null;
        var tangents = await BySlug(page.Tangents, moderators is null ? viewable : [.. viewable, moderators], ct);
        IReadOnlyList<Tangents.Tangent> listed = moderators is not null && page.Tangents.All(tangent => tangent.Id != moderators) && tangents.TryGetValue(moderators, out var reports)
            ? [.. page.Tangents, reports]
            : page.Tangents;
        var people = await People(baseAddress, past.Rows.Select(e => e.ActorId).Append(reader.Id)
            .Concat(past.Rows.Select(e => past.Posts[e.PostId!]).SelectMany(post => post.MentionedIds.Append(post.EditedBy))), ct);
        bool Taken(Event e) => past.Posts[e.PostId!] is { ParentId: null, OriginId: not null };
        var items = past.Rows.Select(e =>
        {
            var (kind, did) = e.Kind switch
            {
                EventKind.Mention => ("mention", "mentioned you"),
                EventKind.PostEdited => ("edited", "edited your post"),
                EventKind.PostRemoved => ("removed", "removed your post"),
                _ when e.Report == true => ("report", "reported"),
                _ when Taken(e) => ("taken", "took this on a tangent from your post"),
                _ => ("reply", "replied to you")
            };
            return new DoorPast.Item(kind, Line(e, past.Posts, tangents, people) with { Did = did, New = false });
        }).ToList();

        Response.Headers.CacheControl = "private";
        var door = baseAddress + "/";
        var back = Uri.EscapeDataString(baseUrl.PathBase + "/");
        return Ok(new DoorPast(
            space.Name,
            door,
            listed.Select(tangent => TangentEntry.Of(tangent, Address(tangent.Id), door)).ToList(),
            page.Next is not null || place.Holds(Bit.Post) ? baseAddress + TangentsController.Route : null,
            place.Holds(Bit.ManageSpace) ? baseAddress + NewTangentController.Route : null,
            Who.Of(Person(people, reader.Id), Roles.Word(place.RoleIds), baseAddress + AuthConstants.Routes.Logout + "?return=" + back,
                TangentSpaceSettingsController.Entry(place, baseAddress),
                new Unread.Badge(forYou.Count, forYou.AtLeast, door, forYou.Through, forYou.Applications)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            new DoorPast.Earlier(items, past.Next is { } next ? OlderAddress(baseAddress, next) : null, door)));
    }

    /// <summary>The name each role these changes name holds now, by key, in one keyed read; a role since deleted has
    /// none.</summary>
    private static async Task<IReadOnlyDictionary<string, string>> RoleNames(IReadOnlyList<Event> changes, CancellationToken ct)
    {
        var keys = changes.Select(e => e.Role).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0) return new Dictionary<string, string>();
        return (await Koan.Identity.Roles.Role.Get(keys.Select(Koan.Identity.Roles.Role.KeyFor).ToList(), ct)).OfType<Koan.Identity.Roles.Role>()
            .ToDictionary(role => role.Key, role => role.Name, StringComparer.Ordinal);
    }

    /// <summary>Whether a new Post is an article's root.</summary>
    private static bool Article(Event e, IReadOnlyDictionary<string, Post> posts)
        => e.PostId == e.TopicId && posts.TryGetValue(e.TopicId!, out var root) && root.Kind == PostKind.Article;

    /// <summary>Where the reader asks again: the poll past the sequence the door read through, or, where it read nothing the
    /// reader may see, past the window's newest, so the position never reaches back before what the door read and says
    /// of hidden events only that a whole window of them passed.</summary>
    private static Door.Position Resume(string baseAddress, long at)
        => new(at, baseAddress + EventsController.Path + "?" + EventsController.Since + "=" + at.ToString(CultureInfo.InvariantCulture));

    /// <summary>One group of new Posts: how many, whether that is a lower bound, and each of them, newest first, at most
    /// <see cref="Unread.CountLimit"/>.</summary>
    private Door.Group Group(IReadOnlyList<Event> events, bool atLeast, IReadOnlyDictionary<string, Post> posts,
        IReadOnlyDictionary<string, Tangents.Tangent> tangents, IReadOnlyDictionary<string, Render.Person> people)
        => new(events.Count, atLeast, events.Take(Unread.CountLimit).Select(e => Line(e, posts, tangents, people)).ToList());

    /// <summary>The Post a row of the log brought the reader, as one line of the news: who, the Topic and Tangent it is in,
    /// its address, its first words and when.</summary>
    private Door.NewPost Line(Event e, IReadOnlyDictionary<string, Post> posts, IReadOnlyDictionary<string, Tangents.Tangent> tangents,
        IReadOnlyDictionary<string, Render.Person> people)
    {
        var post = posts[e.PostId!];
        var root = posts[e.TopicId!];
        var topic = TopicAddress(root);
        return new Door.NewPost(post.Id, Person(people, e.ActorId ?? post.AuthorId), root.Title ?? "", Name(tangents, root.TangentId),
            post.Id == root.Id ? topic : topic + "/" + Uri.EscapeDataString(post.Id), TangentPage.Opening(post, people), e.At, TangentPage.EditedOf(post, people));
    }

    /// <summary>A Topic on the front page, new to the reader by the one rule: as many of its Posts as the window holds
    /// new to them, and whether its moderators closed it or keep it at the top of its Tangent.</summary>
    private Door.Shown Shown(Post root, IReadOnlyDictionary<string, Tangents.Tangent> tangents, IReadOnlyDictionary<string, Render.Person> people,
        IReadOnlyDictionary<string, int> freshIn)
    {
        var unseen = freshIn.GetValueOrDefault(root.Id);
        return new Door.Shown(root.TangentId, Name(tangents, root.TangentId), new TopicEntry(
            root.Id,
            root.Slug ?? root.Id,
            root.Title ?? "",
            Word(root.Kind),
            TopicAddress(root),
            Person(people, root.AuthorId),
            TangentPage.Opening(root, people),
            TangentPage.ReadTime(root.Body),
            root.ReplyCount,
            root.CreatedAt,
            root.LastActivityAt,
            null,
            root.RecentAuthorIds.Take(Post.RecentAuthorLimit).Select(id => Person(people, id)).ToList(),
            unseen > 0,
            unseen,
            TangentPage.EditedOf(root, people))
        {
            Closed = root.Closed,
            Pinned = tangents.TryGetValue(root.TangentId, out var tangent) && tangent.PinnedId == root.Id
        });
    }

    /// <summary>The listed Tangents and the other viewable Tangents the events name, by slug: one keyed read of the
    /// latter.</summary>
    private static async Task<IReadOnlyDictionary<string, Tangents.Tangent>> BySlug(IReadOnlyList<Tangents.Tangent> listed, IReadOnlyList<string> viewable,
        CancellationToken ct)
    {
        var bySlug = listed.ToDictionary(tangent => tangent.Id, StringComparer.Ordinal);
        var missing = viewable.Where(id => !bySlug.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            foreach (var tangent in (await Tangents.Tangent.Get(missing, ct)).OfType<Tangents.Tangent>())
                bySlug[tangent.Id] = tangent;
        return bySlug;
    }

    /// <summary>The Posts the window's events name and the roots of their Topics, by Id, beside those
    /// <paramref name="known"/> already holds: one keyed read of the others.</summary>
    private static async Task<IReadOnlyDictionary<string, Post>> Posts(IReadOnlyList<Event> events, IReadOnlyDictionary<string, Post>? known,
        CancellationToken ct)
    {
        var read = await Unread.Posts(events.Where(e => known is null || !known.ContainsKey(e.PostId!) || !known.ContainsKey(e.TopicId!)), ct);
        return known is null ? read : Merge(read, known);
    }

    private static IReadOnlyDictionary<string, Post> Merge(IReadOnlyDictionary<string, Post> one, IReadOnlyDictionary<string, Post> other)
    {
        var merged = new Dictionary<string, Post>(one, StringComparer.Ordinal);
        foreach (var (id, post) in other) merged.TryAdd(id, post);
        return merged;
    }

    /// <summary>Everyone the door shows, in one keyed read, by participant Id, each with their profile's address.</summary>
    private static async Task<IReadOnlyDictionary<string, Render.Person>> People(string baseAddress, IEnumerable<string?> ids,
        CancellationToken ct)
    {
        var wanted = ids.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0) return new Dictionary<string, Render.Person>();
        return (await Participant.Get(wanted, ct)).OfType<Participant>().ToDictionary(row => row.Id, row => Render.Person.Of(row, baseAddress), StringComparer.Ordinal);
    }

    private static Render.Person Person(IReadOnlyDictionary<string, Render.Person> people, string id)
        => people.TryGetValue(id, out var person) ? person : Render.Person.Unknown(id);

    private static string Name(IReadOnlyDictionary<string, Tangents.Tangent> tangents, string slug)
        => tangents.TryGetValue(slug, out var tangent) ? tangent.Name : slug;

    private string Address(string slug) => BaseAddress() + "/t/" + Uri.EscapeDataString(slug);

    private string TopicAddress(Post root) => Address(root.TangentId) + "/" + Uri.EscapeDataString(root.Slug ?? root.Id);

    private static string Word(PostKind kind) => kind.ToString().ToLowerInvariant();

    private static string? PersonId(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
