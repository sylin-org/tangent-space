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
/// and their writers <see cref="Door.Around"/>; for the place's operator, the open Offer of an address
/// (<see cref="AddressOffers.Open"/>), shown first; for whoever holds <see cref="Bit.ManageStanding"/>, how many
/// applications wait, linked to where they are decided; one seen form whenever the door has news; and the poll link.
/// Everything new is new by the one rule (<see cref="Unread"/>). The door's <c>gate</c> is why the reader may not write
/// in the place, which an applicant's door and a timed-out participant's, with when it ends, say in one sentence. A
/// reader without <see cref="Bit.View"/> on the place receives its gate, worded for reading.</summary>
public sealed class DoorController(KoanWebBaseUrl baseUrl, AddressOffers offers) : ControllerBase
{
    /// <summary>The most Tangents the door lists: the first page of those the reader may view
    /// (<see cref="ViewableTangents"/>), with the moderators' Tangent after them for whoever may view it, and, where more
    /// follow, a link to all of them (<see cref="TangentsController"/>).</summary>
    public const int TangentLimit = TangentPageController.SwitcherLimit;

    public const int FeaturedLimit = 3;

    public const int AliveLimit = 6;

    public const int AroundLimit = 8;

    /// <summary>The most new Posts a group shows; its count says how many there are.</summary>
    public const int ItemLimit = 5;

    [HttpGet("/")]
    [HttpHead("/")]
    [LlmsText.Pointer]
    [Tool(Name = "arrive")]
    [Description("Call this first when you come back, to see who you are here, what is new since you were here and what you may do, with links. Follow with read_tangent on a Tangent's slug, read_topic or read_post on what a new Post's address names, /t/{tangent}/{topic}/{post}, and seen with news.through once you have read it.")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var personId = PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct)
            ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        var reader = personId is null ? null : await Participant.Get(personId, ct);
        var unread = reader is null ? null : await Unread.Of(reader, ct);
        var baseAddress = BaseAddress();

        var forYou = unread is null ? null : await unread.ForYouNow(ct);
        var window = await LogWindow.Newest(ct);
        var page = await ViewableTangents.Read(personId, ViewableTangents.Views, null, TangentLimit, ct);
        var tangentIds = window.Concat(forYou?.Rows ?? []).Select(e => e.TangentId).OfType<string>()
            .Concat(space.ModeratorsTangentId is { } moderators ? [moderators] : [])
            .Distinct(StringComparer.Ordinal).Where(id => !page.Each.ContainsKey(id)).ToList();
        var each = new Dictionary<string, Capabilities>(page.Each, StringComparer.Ordinal);
        foreach (var (id, on) in await Capabilities.LoadEach(personId, tangentIds, ct)) each[id] = on;
        bool Viewable(string? tangentId) => tangentId is not null && each.TryGetValue(tangentId, out var on) && on.Exists;
        var tangents = await BySlug(page.Tangents, tangentIds.Where(Viewable).ToList(), ct);
        // The moderators' Tangent comes after every other, so past the first page of a long list; whoever may view it
        // finds it on the door whatever the place holds.
        var listed = space.ModeratorsTangentId is { } kept && page.Tangents.All(tangent => tangent.Id != kept) && tangents.TryGetValue(kept, out var reports)
            ? [.. page.Tangents, reports]
            : page.Tangents;

        var created = window.Where(e => e.Kind == EventKind.PostCreated && Viewable(e.TangentId) && e.PostId is not null && e.TopicId is not null).ToList();
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
                .Concat((forYouGroups?.Shown() ?? []).Concat(fresh.Where(e => Article(e, posts)).Take(ItemLimit)).Select(e => posts[e.PostId!])
                    .Concat(featured).Concat(alive).SelectMany(post => post.MentionedIds.Append(post.EditedBy))),
            ct);

        Door.Since? news = null;
        var through = 0L;
        if (unread is not null && forYou is not null && forYouGroups is not null)
        {
            // Read through the newest event in a Tangent the reader may view, their own included, and what is for them; an
            // event in a Tangent they may not view moves nothing they can see.
            var newestViewable = window.Where(e => Viewable(e.TangentId)).Select(e => e.Seq).DefaultIfEmpty(0).Max();
            through = new[] { unread.Floor, newestViewable, forYou.Through }.Max();
            news = News(unread, forYou, forYouGroups, fresh, window, posts, tangents, people, through, baseAddress)
                with { Let = await Tangents.Asking.Decided(forYouGroups.Let, people, baseAddress, ct) };
            // A door that carries a form is never stored, so a browser's Back after sign-out redisplays nothing of it; any
            // other door of a signed-in reader is theirs alone to cache.
            if (!news.Quiet && through > unread.Floor) news = news with { Seen = SeenController.DoorForm(baseAddress, through) };
            Response.Headers.CacheControl = news.Seen is null ? "private" : "no-cache, no-store";
        }

        var offered = reader is not null && reader.Id == space.OperatorId ? await offers.Offered(space, baseAddress, ct) : null;
        if (offered is not null) Response.Headers.CacheControl = "no-cache, no-store";

        // Whoever decides applications reads how many wait, seen or not, and reaches them in one click.
        var waiting = reader is not null && place.Holds(Bit.ManageStanding)
            ? await Applications.Waiting(0, long.MaxValue, Unread.CountLimit + 1, ct)
            : [];

        var asking = reader is null ? [] : await Tangents.Asking.For(reader.Id, baseAddress, ct);

        var freshIn = fresh.GroupBy(e => e.TopicId!, StringComparer.Ordinal).ToDictionary(topic => topic.Key, topic => topic.Count(), StringComparer.Ordinal);
        var newIn = news?.PerTangent.ToDictionary(tangent => tangent.Slug, tangent => tangent.Topics + tangent.Replies, StringComparer.Ordinal);
        var door = baseAddress + "/";
        var back = Uri.EscapeDataString(baseUrl.PathBase + "/");
        return Ok(new Door(
            space.Name,
            door,
            space.ProprietorId is { } proprietor ? Person(people, proprietor) : null,
            place.Can,
            news,
            listed
                .Select(tangent => new Door.Listed(tangent.Id, tangent.Name, Word(tangent.DefaultKind), Address(tangent.Id), tangent.TopicCount,
                    newIn?.GetValueOrDefault(tangent.Id) ?? 0))
                .ToList(),
            page.Next is not null,
            featured.Select(root => Shown(root, tangents, people, freshIn)).ToList(),
            alive.Select(root => Shown(root, tangents, people, freshIn)).ToList(),
            new Door.Crowd(writers.Count, window.Count >= LogWindow.Size, writers.Take(AroundLimit).Select(id => Person(people, id)).ToList()),
            reader is null ? baseAddress + SignInController.Path + "?return=" + back : null,
            reader is null ? null : new Door.Reader(reader.Id, Person(people, reader.Id).Name, Person(people, reader.Id).Handle,
                Roles.Word(place.RoleIds), reader.Profile.Avatar, baseAddress + AuthConstants.Routes.Logout + "?return=" + back,
                TangentSpaceSettingsController.Entry(place, baseAddress),
                forYou is null ? null : new Unread.Badge(forYou.Count, forYou.AtLeast, door, forYou.Through, forYou.Applications)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            Resume(baseAddress, through > 0 ? through : window.Count > 0 ? window[0].Seq : 0),
            place.Holds(Bit.ManageSpace) ? baseAddress + NewTangentController.Route : null)
        {
            Description = space.Description,
            Intro = space.Slots.DoorIntro.Length > 0 ? Media.Placed(space.Slots.DoorIntro, baseAddress + "/") : null,
            Feeds = space.Audience == Audience.Public ? Feeds.ArticlesLinks(baseAddress, space.Name) : null,
            Offer = offered,
            Applications = waiting.Count == 0
                ? null
                : new Door.Waiting(waiting.Count, waiting.Count > Unread.CountLimit, baseAddress + ApplicationsController.Path),
            Asks = asking.Count == 0 ? null : asking,
            AllTangents = page.Next is null ? null : baseAddress + TangentsController.Route,
            Gate = place.Gate(Bit.Post)?.Word,
            Until = reader is not null && reader.StandingAt(now) == Standing.TimedOut ? reader.StandingUntil : null
        });
    }

    /// <summary>The Posts the window's new Topics and replies brought that are new to the reader by the one rule: in a
    /// Tangent they may view, someone else's, standing in a Topic that stands, past where they were last here and past
    /// their mark on the Topic.</summary>
    private static List<Event> Fresh(IReadOnlyList<Event> created, Unread unread, IReadOnlyDictionary<string, Post> posts,
        IReadOnlyDictionary<string, long> marks)
        => created.Where(e => posts.TryGetValue(e.PostId!, out var post) && !post.Removed
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

        /// <summary>The rows the door shows with a Post's first words.</summary>
        public IEnumerable<Event> Shown() => Replies.Take(ItemLimit).Concat(Taken.Take(ItemLimit)).Concat(Mentions.Take(ItemLimit))
            .Concat(Reports.Take(ItemLimit)).Concat(Edited.Take(ItemLimit));
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
    /// articles, each count a lower bound where a read it came from was full.</summary>
    private Door.Since News(Unread unread, Unread.ForYou forYou, ForYouGroups groups, IReadOnlyList<Event> fresh, IReadOnlyList<Event> window,
        IReadOnlyDictionary<string, Post> posts, IReadOnlyDictionary<string, Tangents.Tangent> tangents,
        IReadOnlyDictionary<string, TangentPage.Author> people, long through, string baseAddress)
    {
        var partial = LogWindow.Partial(window, unread.Floor);
        Door.Group Group(IReadOnlyList<Event> events, bool atLeast) => this.Group(events, atLeast, posts, tangents, people);
        return new Door.Since(
            unread.Floor,
            through,
            Group(groups.Replies, forYou.AtLeast),
            Group(groups.Mentions, forYou.AtLeast),
            fresh.GroupBy(e => e.TangentId!, StringComparer.Ordinal)
                .Select(byTangent => new Door.InTangent(byTangent.Key, Name(tangents, byTangent.Key), Address(byTangent.Key),
                    byTangent.Count(e => e.PostId == e.TopicId), byTangent.Count(e => e.PostId != e.TopicId), partial))
                .ToList(),
            Group(fresh.Where(e => Article(e, posts)).ToList(), partial),
            null,
            Group(groups.Taken, forYou.AtLeast),
            groups.Admitted is { } admitted ? new Door.Decision(Person(people, admitted.ActorId!), admitted.At) : null,
            groups.Lifted is { } lifted ? new Door.Decision(Person(people, lifted.ActorId!), lifted.At, Participants.People.Word(lifted.Before!.Value)) : null,
            groups.Roles.Select(e => new Door.RoleChange(Person(people, e.ActorId!), e.At, Roles.Word(e.Role!), e.Given!.Value)).ToList(),
            Group(groups.Edited, forYou.AtLeast),
            Group(groups.Removed, forYou.AtLeast),
            Group(groups.Reports, forYou.AtLeast),
            groups.TimedOut is { } timedOut ? new Door.Decision(Person(people, timedOut.ActorId!), timedOut.At, Until: unread.Reader.StandingUntil) : null,
            groups.Declined is { } declined ? new Door.Decision(Person(people, declined.ActorId!), declined.At) : null,
            forYou.Waiting > 0 ? new Door.Waiting(forYou.Waiting, forYou.AtLeast, baseAddress + ApplicationsController.Path) : null);
    }

    /// <summary>Whether a new Post is an article's root.</summary>
    private static bool Article(Event e, IReadOnlyDictionary<string, Post> posts)
        => e.PostId == e.TopicId && posts.TryGetValue(e.TopicId!, out var root) && root.Kind == PostKind.Article;

    /// <summary>Where the reader asks again: the poll past the sequence the door read through, or, where it read nothing the
    /// reader may see, past the window's newest, so the position never reaches back before what the door read and says
    /// of hidden events only that a whole window of them passed.</summary>
    private static Door.Position Resume(string baseAddress, long at)
        => new(at, baseAddress + EventsController.Path + "?" + EventsController.Since + "=" + at.ToString(CultureInfo.InvariantCulture));

    /// <summary>One group of new Posts: how many, whether that is a lower bound, and the newest few.</summary>
    private Door.Group Group(IReadOnlyList<Event> events, bool atLeast, IReadOnlyDictionary<string, Post> posts,
        IReadOnlyDictionary<string, Tangents.Tangent> tangents, IReadOnlyDictionary<string, TangentPage.Author> people)
        => new(events.Count, atLeast, events.Take(ItemLimit).Select(e =>
        {
            var post = posts[e.PostId!];
            var root = posts[e.TopicId!];
            var topic = TopicAddress(root);
            return new Door.NewPost(post.Id, Person(people, e.ActorId ?? post.AuthorId), root.Title ?? "", Name(tangents, root.TangentId),
                post.Id == root.Id ? topic : topic + "/" + Uri.EscapeDataString(post.Id), TangentPage.Opening(post, people), e.At, TangentPage.EditedOf(post, people));
        }).ToList());

    /// <summary>A Topic on the front page, new to the reader by the one rule: as many of its Posts as the window holds
    /// new to them.</summary>
    private Door.Shown Shown(Post root, IReadOnlyDictionary<string, Tangents.Tangent> tangents, IReadOnlyDictionary<string, TangentPage.Author> people,
        IReadOnlyDictionary<string, int> freshIn)
    {
        var unseen = freshIn.GetValueOrDefault(root.Id);
        return new Door.Shown(root.TangentId, Name(tangents, root.TangentId), new TangentPage.Topic(
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
            TangentPage.EditedOf(root, people)));
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
    private static async Task<IReadOnlyDictionary<string, TangentPage.Author>> People(string baseAddress, IEnumerable<string?> ids,
        CancellationToken ct)
    {
        var wanted = ids.OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0) return new Dictionary<string, TangentPage.Author>();
        return (await Participant.Get(wanted, ct)).OfType<Participant>().ToDictionary(row => row.Id, row =>
        {
            var handle = row.Handle.Length > 0 ? row.Handle : row.Id;
            var name = row.Profile.DisplayName is { Length: > 0 } displayName ? displayName : handle;
            return new TangentPage.Author(row.Id, handle, name, row.Kind.ToString().ToLowerInvariant(), row.Profile.Avatar,
                ProfilePage.AddressOf(baseAddress, handle));
        }, StringComparer.Ordinal);
    }

    private static TangentPage.Author Person(IReadOnlyDictionary<string, TangentPage.Author> people, string id)
        => people.TryGetValue(id, out var person) ? person : new TangentPage.Author(id, id, id, "person", null);

    private static string Name(IReadOnlyDictionary<string, Tangents.Tangent> tangents, string slug)
        => tangents.TryGetValue(slug, out var tangent) ? tangent.Name : slug;

    private string Address(string slug) => BaseAddress() + "/t/" + Uri.EscapeDataString(slug);

    private string TopicAddress(Post root) => Address(root.TangentId) + "/" + Uri.EscapeDataString(root.Slug ?? root.Id);

    private static string Word(PostKind kind) => kind.ToString().ToLowerInvariant();

    private static string? PersonId(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
