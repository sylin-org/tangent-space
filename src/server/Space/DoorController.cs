using System.Security.Claims;
using Koan.Data.Abstractions;
using Koan.Data.Core.Sorting;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Events;
using Tangent.Offers;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Tangents;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>The door, one resource both ways, at the base URL. Its <c>GET</c> writes nothing and reads, each bounded
/// and keyed: capability's rows for the place; the reader's place checkpoint; the newest events of the log and the
/// newest that name the reader as their target (<see cref="LogWindow"/>); how many of the reader's notifications they have
/// not seen (<see cref="Notifications.Count"/>); the first Tangents by order; capability once
/// per distinct Tangent among them and the events; the Posts and Topics the events name; the reader's checkpoints for
/// those Topics and Tangents; and the people it shows. An event is new to the reader when it is past their place
/// checkpoint, past their checkpoint for its Topic (in the Topic's own sequence) and for its Tangent where those exist,
/// not their own, and in a Tangent they may view; an event in a Tangent they may not view does not exist for them. A
/// mention is not judged by the Topic's checkpoint: that counts the Topic's Posts as they arrived, and an edit can
/// mention the reader in a Post they read long before, so a mention is new by its own place in the log alone.
/// The Topics and Tangents among the news that the reader watches, by the watch flag on those same checkpoints, are
/// listed first. The newest <c>standing.changed</c> among the reader's targeted events by someone else is news while they
/// stand admitted, as their admission or as the lift of their timeout or ban, by the standing it changed; so is each
/// <c>role.changed</c> that gave them a role or took one, the newest of each role. Where the reader's unseen
/// notifications are more than the door lists of what targets them, or a count it could not finish, the news names
/// their notifications. The door's <c>gate</c> is why they may
/// not write in the place, which an applicant's door and a timed-out participant's, with when it ends, say in one
/// sentence.
/// The front page comes from the same window: the Topics that gained a Post in it, newest first, pinned ones and
/// articles <see cref="Door.Featured"/> and the rest <see cref="Door.Alive"/>, each new to the reader by the same
/// rule, and its writers <see cref="Door.Around"/>. For the place's operator, while no URL is configured, it also reads
/// the open Offers they decide, one bounded read, with the evidence Koan holds for each, and shows them first. A reader
/// without <see cref="Bit.View"/> on the place receives its gate. Its <c>POST</c>
/// moves the reader's place checkpoint to the sequence a door read through, through the writer.</summary>
public sealed class DoorController(KoanWebBaseUrl baseUrl, WriteLoop writer, AddressOffers offers) : ControllerBase
{
    /// <summary>The form field that carries the sequence the door was read through.</summary>
    public const string Through = "through";

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
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var personId = PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return Gated(place.Gate(Bit.View) ?? Capabilities.Absent);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct)
            ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        var reader = personId is null ? null : await Participant.Get(personId, ct);

        var since = reader is null ? 0 : (await Checkpoint.Get(Checkpoint.Key(reader.Id, Scope.Place), ct))?.Seq ?? 0;
        var window = await LogWindow.Newest(ct);
        IReadOnlyList<Event> targeted = reader is null ? [] : await LogWindow.Targeting(reader.Id, since, ct);

        var page = await ViewableTangents.Read(personId, ViewableTangents.Views, null, TangentLimit, ct);
        var tangentIds = window.Concat(targeted).Select(e => e.TangentId).OfType<string>()
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

        // The events that speak of Posts in a Tangent the reader may view; every other one is absent here.
        var concerning = window.Concat(targeted)
            .Where(e => e.Kind is EventKind.PostCreated or EventKind.Mention && Viewable(e.TangentId) && e.PostId is not null && e.TopicId is not null)
            .ToList();
        var posts = await Posts(concerning, ct);
        var seen = reader is null ? new Dictionary<string, Checkpoint>() : await Seen(reader.Id, concerning, ct);

        bool New(Event e) =>
            reader is not null && e.Seq > since && e.ActorId != reader.Id && Viewable(e.TangentId)
            && posts.TryGetValue(e.PostId ?? "", out var post) && !post.Removed
            && posts.TryGetValue(e.TopicId ?? "", out var root) && !root.Removed
            && (e.Kind == EventKind.Mention
                || !seen.TryGetValue(Checkpoint.Key(reader.Id, Scope.Topic(root.Id)), out var topicSeen) || post.Sequence > topicSeen.Seq)
            && (!seen.TryGetValue(Checkpoint.Key(reader.Id, Scope.Tangent(root.TangentId)), out var tangentSeen) || e.Seq > tangentSeen.Seq);
        bool Watched(Scope scope) => reader is not null && seen.TryGetValue(Checkpoint.Key(reader.Id, scope), out var checkpoint) && checkpoint.Watch;

        var created = window.Where(e => e.Kind == EventKind.PostCreated && Viewable(e.TangentId)).ToList();
        var active = created.Select(e => e.TopicId).OfType<string>().Distinct(StringComparer.Ordinal)
            .Select(id => posts.GetValueOrDefault(id)).OfType<Post>().Where(root => !root.Removed).ToList();
        var featured = active.Where(root => root.Pinned || root.Kind == PostKind.Article).Take(FeaturedLimit).ToList();
        var alive = active.Where(root => !root.Pinned && root.Kind != PostKind.Article).Take(AliveLimit).ToList();
        var writers = created.Select(e => e.ActorId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var full = window.Count >= LogWindow.Size;

        var fresh = window.Where(e => e.Kind == EventKind.PostCreated && New(e)).ToList();
        var freshIn = fresh.GroupBy(e => e.TopicId!, StringComparer.Ordinal).ToDictionary(topic => topic.Key, topic => topic.Count(), StringComparer.Ordinal);
        // A Topic taken on a tangent from the reader's Post targets them too; its root is not a reply, so it is a group of
        // its own.
        bool TakenOnATangent(Event e) => posts.TryGetValue(e.PostId ?? "", out var post) && post is { ParentId: null, OriginId: not null };
        var replies = targeted.Where(e => e.Kind == EventKind.PostCreated && e.TargetId == reader?.Id && New(e) && !TakenOnATangent(e)).ToList();
        var taken = targeted.Where(e => e.Kind == EventKind.PostCreated && e.TargetId == reader?.Id && New(e) && TakenOnATangent(e)).ToList();
        var mentions = targeted.Where(e => e.Kind == EventKind.Mention && e.TargetId == reader?.Id && New(e)).ToList();
        var articles = fresh.Where(e => e.PostId == e.TopicId && posts[e.TopicId!].Kind == PostKind.Article).ToList();
        // Someone admitted the reader, or lifted their timeout or ban, since their checkpoint, and they stand admitted
        // still: the newest change someone else made of their standing says which.
        var now = DateTimeOffset.UtcNow;
        var decided = reader is not null && reader.StandingAt(now) == Standing.Admitted
            ? targeted.FirstOrDefault(e => e is { Kind: EventKind.StandingChanged, ActorId: not null } && e.ActorId != reader.Id)
            : null;
        var admitted = decided is { Standing: Standing.Admitted, Before: not (Standing.TimedOut or Standing.Banned) } ? decided : null;
        var lifted = decided is { Standing: Standing.Admitted, Before: Standing.TimedOut or Standing.Banned } ? decided : null;
        // The roles someone gave the reader or took from them since their checkpoint, the newest change of each.
        var roleChanges = reader is null
            ? []
            : targeted.Where(e => e is { Kind: EventKind.RoleChanged, ActorId: not null, Role: not null, Given: not null } && e.ActorId != reader.Id
                                  && Participants.People.Given.Any(given => given.Key == e.Role))
                .GroupBy(e => e.Role!, StringComparer.Ordinal).Select(changes => changes.First()).ToList();

        var baseAddress = BaseAddress();
        var people = await People(
            baseAddress,
            new[] { space.ProprietorId, reader?.Id, admitted?.ActorId, lifted?.ActorId }.Concat(roleChanges.Select(e => e.ActorId))
                .Concat(replies.Concat(mentions).Concat(articles).Concat(taken).Select(e => e.ActorId))
                .Concat(writers.Take(AroundLimit))
                .Concat(featured.Concat(alive).SelectMany(root => root.RecentAuthorIds.Take(Post.RecentAuthorLimit).Append(root.AuthorId)))
                .Concat(replies.Take(ItemLimit).Concat(mentions.Take(ItemLimit)).Concat(articles.Take(ItemLimit)).Concat(taken.Take(ItemLimit))
                    .Select(e => posts[e.PostId!])
                    .Concat(featured).Concat(alive).SelectMany(post => post.MentionedIds)),
            ct);

        var door = baseAddress + "/";
        var back = Uri.EscapeDataString(baseUrl.PathBase + "/");
        // Read through the newest event in a Tangent the reader may view, their own included, and the changes of their
        // standing and roles the door tells; an event in a Tangent they may not view moves nothing they can see.
        var newestViewable = window.Concat(targeted).Where(e => Viewable(e.TangentId) || e == admitted || e == lifted || roleChanges.Contains(e))
            .Select(e => e.Seq).DefaultIfEmpty(0).Max();
        var through = Math.Max(since, newestViewable);
        Door.Since? news = null;
        if (reader is not null)
        {
            var partial = LogWindow.Partial(window, since);
            var targetedFull = targeted.Count >= LogWindow.TargetedSize;
            news = new Door.Since(
                since,
                through,
                WatchedNews(fresh, Watched, posts, tangents, partial),
                Group(replies, targetedFull, posts, tangents, people),
                Group(mentions, targetedFull, posts, tangents, people),
                fresh.GroupBy(e => e.TangentId!, StringComparer.Ordinal)
                    .Select(byTangent => new Door.InTangent(byTangent.Key, Name(tangents, byTangent.Key), Address(byTangent.Key),
                        byTangent.Count(e => e.PostId == e.TopicId), byTangent.Count(e => e.PostId != e.TopicId), partial))
                    .ToList(),
                Group(articles, partial, posts, tangents, people),
                null,
                Group(taken, targetedFull, posts, tangents, people),
                admitted is null ? null : new Door.Decision(Person(people, admitted.ActorId!), admitted.At),
                lifted is null ? null : new Door.Decision(Person(people, lifted.ActorId!), lifted.At, Participants.People.Word(lifted.Before!.Value)),
                roleChanges.Select(e => new Door.RoleChange(Person(people, e.ActorId!), e.At,
                    Participants.People.Given.First(given => given.Key == e.Role).Word, e.Given!.Value)).ToList());
            // A door that carries the form carries a fresh antiforgery token, so it is never stored or revalidated; any
            // other door of a signed-in reader is theirs alone to cache.
            if (!news.Quiet) news = news with { CaughtUp = new Door.CaughtUp(door, through, FormToken.For(HttpContext)) };
            Response.Headers.CacheControl = news.CaughtUp is null ? "private" : "no-cache, no-store";
        }

        var offered = reader is not null && reader.Id == space.OperatorId && !baseUrl.IsConfigured ? await Offered(reader.Id, baseAddress, ct) : null;
        if (offered is not null) Response.Headers.CacheControl = "no-cache, no-store";

        // The door names the reader's notifications where they hold more unseen than the door lists of what targets them.
        var unseen = reader is null ? null : await Notifications.Count(reader.Id, baseAddress, ct);
        if (news is not null && unseen is not null)
        {
            var shown = replies.Take(ItemLimit).Count() + mentions.Take(ItemLimit).Count() + taken.Take(ItemLimit).Count()
                        + (admitted is null ? 0 : 1) + (lifted is null ? 0 : 1) + roleChanges.Count + (offered?.Count ?? 0);
            if (unseen.AtLeast || unseen.Count > shown) news = news with { Notifications = unseen.Address };
        }

        var newIn = news?.PerTangent.ToDictionary(tangent => tangent.Slug, tangent => tangent.Topics + tangent.Replies, StringComparer.Ordinal);
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
            new Door.Crowd(writers.Count, full, writers.Take(AroundLimit).Select(id => Person(people, id)).ToList()),
            reader is null ? baseAddress + SignInController.Path + "?return=" + back : null,
            reader is null ? null : new Door.Reader(reader.Id, Person(people, reader.Id).Name, Person(people, reader.Id).Handle,
                Roles.Word(place.RoleIds), reader.Profile.Avatar, baseAddress + AuthConstants.Routes.Logout + "?return=" + back,
                TangentSpaceSettingsController.Entry(place, baseAddress), unseen),
            DateOnly.FromDateTime(DateTime.UtcNow),
            Resume(baseAddress, newestViewable > 0 ? through : Math.Max(since, window.Count > 0 ? window[0].Seq : 0)),
            place.Holds(Bit.ManageSpace) ? baseAddress + NewTangentController.Route : null)
        {
            Description = space.Description,
            Offers = offered,
            AllTangents = page.Next is null ? null : baseAddress + TangentsController.Route,
            Gate = place.Gate(Bit.Post),
            Until = reader is not null && reader.StandingAt(now) == Standing.TimedOut ? reader.StandingUntil : null
        });
    }

    /// <summary>The open Offers <paramref name="deciderId"/> decides, each with what Koan has seen of its subject and its
    /// two answers under <paramref name="baseAddress"/>; null when none is open.</summary>
    private async Task<IReadOnlyList<Door.Offered>?> Offered(string deciderId, string baseAddress, CancellationToken ct)
    {
        var open = await Offer.Query(offer => offer.State == OfferState.Open && offer.DeciderId == deciderId,
            new QueryDefinition { Page = 1, PageSize = Offer.OpenLimit }, ct);
        if (open.Count == 0) return null;
        var token = FormToken.For(HttpContext);
        return open.Select(offer =>
        {
            var seen = offers.Evidence(offer.Subject);
            return new Door.Offered(offer.Id, Offer.Word(offer.Kind), offer.Subject, seen?.Count, seen?.FirstSeen,
                baseAddress + OfferController.UsePath, baseAddress + OfferController.DeclinePath, token);
        }).ToList();
    }

    /// <summary>Where the reader asks again: the poll past the newest event the door read that they may view, or, where its
    /// whole window held none, past the window's newest, so the position never reaches back before what the door read and
    /// says of hidden events only that a whole window of them passed.</summary>
    private static Door.Position Resume(string baseAddress, long at)
        => new(at, baseAddress + EventsController.Path + "?" + EventsController.Since + "=" + at.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Marks the door seen: moves the signed-in reader's place checkpoint to the sequence in the
    /// <see cref="Through"/> field, never backwards, and sends them back to the door. The form answers to
    /// <see cref="FormToken.Validate"/>, a session with its antiforgery token, before anything else is read.</summary>
    [HttpPost("/")]
    public async Task<IActionResult> MarkSeen([FromForm(Name = Through)] string? through, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (PersonId(User) is not { } personId)
            return new Problem(StatusCodes.Status401Unauthorized, "Sign in first", "Marking the door seen needs a signed-in participant.",
                Capabilities.SignIn).ToResult();
        if (!await FormToken.Validate(HttpContext))
            return new Problem(StatusCodes.Status400BadRequest, "Open the door again",
                "This form’s token is missing or has expired, so nothing was marked seen.").ToResult();

        if (!long.TryParse(through, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var sequence))
            return new Problem(StatusCodes.Status400BadRequest, "Not a sequence of the log",
                $"The {Through} field carries the sequence the door names as read through.").ToResult();

        switch (await writer.Send(new CatchUp(personId, sequence), ct))
        {
            case Outcome<Checkpoint>.Done:
                Response.Headers.Location = baseUrl.PathBase + "/";
                return StatusCode(StatusCodes.Status303SeeOther);
            case Outcome<Checkpoint>.Refused { Reason: CatchUp.NoPlace }:
                return Gated(Capabilities.Absent);
            case Outcome<Checkpoint>.Refused refused:
                return new Problem(StatusCodes.Status400BadRequest, "Not a sequence of the log", refused.Reason).ToResult();
            case Outcome<Checkpoint>.Failed failed:
                throw new InvalidOperationException($"Marking the door seen for {personId} failed.", failed.Cause);
            case var other:
                throw new InvalidOperationException($"The writer answered {other} for marking the door seen for {personId}.");
        }
    }

    /// <summary>What a reader without <see cref="Bit.View"/> on the place receives: a missing resource's answer where
    /// the place is absent to them; otherwise the one step that stands between them and reading.</summary>
    internal static IActionResult Gated(string gate) => gate switch
    {
        Capabilities.Absent => new Problem(StatusCodes.Status404NotFound, "Not found", "Nothing answers at this address.", gate).ToResult(),
        Capabilities.SignIn => new Problem(StatusCodes.Status401Unauthorized, "Sign in to read",
            "This Tangent Space is read by its participants.", gate).ToResult(),
        Capabilities.Apply => new Problem(StatusCodes.Status403Forbidden, "Waiting to be admitted",
            "Your application is waiting: you can read here once someone admits you.", gate).ToResult(),
        Capabilities.Declined => new Problem(StatusCodes.Status403Forbidden, "Not admitted",
            "Your application was declined, so this Tangent Space is not open to you.", gate).ToResult(),
        _ => new Problem(StatusCodes.Status403Forbidden, "Not readable", "This Tangent Space is not open to you.", gate).ToResult()
    };

    /// <summary>One group of new Posts: how many, whether that is a lower bound, and the newest few.</summary>
    private Door.Group Group(IReadOnlyList<Event> events, bool atLeast, IReadOnlyDictionary<string, Post> posts,
        IReadOnlyDictionary<string, Tangents.Tangent> tangents, IReadOnlyDictionary<string, TangentPage.Author> people)
        => new(events.Count, atLeast, events.Take(ItemLimit).Select(e =>
        {
            var post = posts[e.PostId!];
            var root = posts[e.TopicId!];
            var topic = TopicAddress(root);
            return new Door.NewPost(post.Id, Person(people, e.ActorId ?? post.AuthorId), root.Title ?? "", Name(tangents, root.TangentId),
                post.Id == root.Id ? topic : topic + "/" + Uri.EscapeDataString(post.Id), TangentPage.Opening(post, people), e.At);
        }).ToList());

    /// <summary>A Topic on the front page, new to the reader by the door's own rule: as many of its Posts as the
    /// window holds new to them.</summary>
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
            root.Pinned,
            root.Locked,
            null,
            root.RecentAuthorIds.Take(Post.RecentAuthorLimit).Select(id => Person(people, id)).ToList(),
            unseen > 0,
            unseen));
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

    /// <summary>The Posts the events name and the roots of their Topics, by Id, in one keyed read.</summary>
    private static async Task<IReadOnlyDictionary<string, Post>> Posts(IReadOnlyList<Event> events, CancellationToken ct)
    {
        var ids = events.SelectMany(e => new[] { e.PostId!, e.TopicId! }).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Post>();
        return (await Post.Get(ids, ct)).OfType<Post>().ToDictionary(post => post.Id, StringComparer.Ordinal);
    }

    /// <summary>The watched Topics and Tangents among the new Posts, each with how many of them it holds, the one whose
    /// newest Post is newest first: a Topic the reader watches, then the Tangent they watch, by the same order.</summary>
    private IReadOnlyList<Door.Watched> WatchedNews(IReadOnlyList<Event> fresh, Func<Scope, bool> watched, IReadOnlyDictionary<string, Post> posts,
        IReadOnlyDictionary<string, Tangents.Tangent> tangents, bool atLeast)
    {
        var topics = fresh.GroupBy(e => e.TopicId!, StringComparer.Ordinal)
            .Where(topic => watched(Scope.Topic(topic.Key)))
            .Select(topic => (Newest: topic.Max(e => e.Seq), Item: new Door.Watched("topic", posts[topic.Key].Title ?? "",
                Name(tangents, posts[topic.Key].TangentId), TopicAddress(posts[topic.Key]), topic.Count(), atLeast)));
        var inTangents = fresh.GroupBy(e => e.TangentId!, StringComparer.Ordinal)
            .Where(tangent => watched(Scope.Tangent(tangent.Key)))
            .Select(tangent => (Newest: tangent.Max(e => e.Seq), Item: new Door.Watched("tangent", Name(tangents, tangent.Key),
                Name(tangents, tangent.Key), Address(tangent.Key), tangent.Count(), atLeast)));
        return topics.OrderByDescending(topic => topic.Newest).Concat(inTangents.OrderByDescending(tangent => tangent.Newest))
            .Select(watch => watch.Item).ToList();
    }

    /// <summary>The reader's checkpoints for the Topics and Tangents the events are in, by key, in one keyed read.</summary>
    private static async Task<IReadOnlyDictionary<string, Checkpoint>> Seen(string readerId, IReadOnlyList<Event> events, CancellationToken ct)
    {
        var keys = events.Select(e => Checkpoint.Key(readerId, Scope.Topic(e.TopicId!)))
            .Concat(events.Select(e => Checkpoint.Key(readerId, Scope.Tangent(e.TangentId!))))
            .Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0) return new Dictionary<string, Checkpoint>();
        return (await Checkpoint.Get(keys, ct)).OfType<Checkpoint>().ToDictionary(checkpoint => checkpoint.Id, StringComparer.Ordinal);
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
