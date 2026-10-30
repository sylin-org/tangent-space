using Koan.Web.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Delegates;
using Tangent.Offers;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;

namespace Tangent.Events;

/// <summary>The signed-in reader's notifications at <see cref="Notifications.Path"/>, one resource both ways
/// (<see cref="NotificationsPage"/>): a page of them newest first (<see cref="Notifications.Read"/>), continued from a
/// sealed cursor that is refused with 400 when it does not open, each one line about one thing — the Post, the Topic, the
/// report in the moderators' Tangent, naming the reported Post's author where the reader may view that Post, the door,
/// the reader's profile, where the place answers, or, for an application that waits, the applications — with an open
/// Offer's two answers inline. The person opening them in their own session moves their notifications checkpoint
/// through the newest the page shows (<see cref="SeeNotifications"/>), so the count every page's frame shows reads past
/// it; it never moves backwards, and it decides no Offer and no application. The mark is handed to the writer only where
/// its queue is not full and is never waited on: the page answers whatever becomes of it, and a later opening makes good
/// one that did not land. A delegate reads them as its person does and moves nothing, since seen means the person saw
/// them. The page is never stored. A visitor is asked to sign in, and a reader without <see cref="Bit.View"/> on the
/// place receives its gate.</summary>
public sealed class NotificationsController(KoanWebBaseUrl baseUrl, WriteLoop writer, IDataProtectionProvider protection,
    [FromKeyedServices(Readable.Clock)] TimeProvider clock) : ControllerBase
{
    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    [HttpGet(Notifications.Path)]
    public async Task<IActionResult> Get([FromQuery(Name = After)] string? after, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return DoorController.Gated(place.Gate(Bit.View) ?? Capabilities.Absent);
        if (personId is null)
            return new Problem(StatusCodes.Status401Unauthorized, "Sign in first", "Notifications are a signed-in participant's.",
                Capabilities.SignIn).ToResult();

        Notifications.Cursor? cursor = null;
        if (after is not null && (cursor = Notifications.Cursor.Open(after, protection)) is null)
            return new Problem(StatusCodes.Status400BadRequest, "Not a page of your notifications",
                $"The {After} parameter is the cursor a page of your notifications names as its next.").ToResult();

        var seen = await Notifications.Seen(personId, ct);
        var from = cursor?.From ?? seen;
        var page = await Notifications.Read(personId, cursor?.Before, ct, clock);
        var newest = page.Events.Count > 0 ? page.Events[0].Seq : 0;
        // Only the person's own opening marks them seen, and it never waits for the writer to do it.
        var marked = newest > seen && !DelegateIdentity.Delegated(HttpContext) && writer.TrySend(new SeeNotifications(personId, newest)) is not null;
        var through = marked ? newest : seen;

        var baseAddress = baseUrl.Resolve(HttpContext);
        var address = baseAddress + Notifications.Path;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        var reader = await PostWrites.Reader(personId, place, baseAddress, baseUrl.PathBase + Notifications.Path, ct);
        // The frame counts past the mark this page handed the writer, whether or not it has landed yet.
        if (marked && reader is not null) reader = reader with { Notifications = await Notifications.CountPast(personId, through, baseAddress, ct) };
        var entries = await Entries(page.Events, from, reader, space, baseAddress, ct);
        return Ok(new NotificationsPage(
            space.Name,
            baseAddress + "/",
            address,
            from,
            through,
            entries,
            page.Next is { } next ? address + "?" + After + "=" + Uri.EscapeDataString(new Notifications.Cursor(next, from).Seal(protection)) : null,
            cursor is not null,
            reader,
            DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    /// <summary>Each notification as the page shows it, from keyed reads of what the events name: their Posts and the
    /// roots of their Topics, their Tangents, the Offers, the Posts the reports report (<see cref="ReportedBy"/>) and the
    /// people.</summary>
    private async Task<IReadOnlyList<NotificationsPage.Entry>> Entries(IReadOnlyList<Event> events, long from, Who? reader, TangentSpace space,
        string baseAddress, CancellationToken ct)
    {
        if (events.Count == 0) return [];
        var postIds = events.Where(e => e.PostId is not null && e.TopicId is not null)
            .SelectMany(e => new[] { e.PostId!, e.TopicId! }).Distinct(StringComparer.Ordinal).ToList();
        var posts = postIds.Count == 0
            ? new Dictionary<string, Post>()
            : (await Post.Get(postIds, ct)).OfType<Post>().ToDictionary(post => post.Id, StringComparer.Ordinal);
        var tangentIds = events.Select(e => e.TangentId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var tangents = tangentIds.Count == 0
            ? new Dictionary<string, Tangents.Tangent>()
            : (await Tangents.Tangent.Get(tangentIds, ct)).OfType<Tangents.Tangent>().ToDictionary(tangent => tangent.Id, StringComparer.Ordinal);
        var offerIds = events.Select(e => e.OfferId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var offers = offerIds.Count == 0
            ? new Dictionary<string, Offer>()
            : (await Offer.Get(offerIds, ct)).OfType<Offer>().ToDictionary(offer => offer.Id, StringComparer.Ordinal);
        var reported = await ReportedBy(events, posts, reader?.Id, ct);
        var people = await People(baseAddress,
            events.Select(e => e.ActorId).Concat(events.Where(e => e.PostId is not null)
                .SelectMany(e => posts.TryGetValue(e.PostId!, out var post) ? post.MentionedIds : []))
                .Concat(reported.Values.Select(origin => origin.AuthorId)),
            ct);

        var token = offers.Values.Any(offer => offer.State == OfferState.Open) ? FormToken.For(HttpContext) : null;
        var door = baseAddress + "/";
        var profile = reader is null ? door : ProfilePage.AddressOf(baseAddress, reader.Handle);
        var answers = !baseUrl.IsConfigured && reader?.Id == space.OperatorId;
        return events.Select(e =>
        {
            var isNew = e.Seq > from;
            var who = e.ActorId is { } actor ? Person(people, actor) : null;
            switch (e.Kind)
            {
                case EventKind.PostCreated when e.Report == true && posts.TryGetValue(e.PostId ?? "", out var report)
                                                                 && posts.TryGetValue(e.TopicId ?? "", out var reportRoot):
                    // Someone reported a Post, and the report reaches whoever may view the moderators' Tangent: the
                    // reader, among them. The reported Post's author is named only where the reader may view it.
                    var reportTopic = PostWrites.TopicAddress(baseAddress, reportRoot.TangentId, reportRoot.Slug ?? reportRoot.Id);
                    return new NotificationsPage.Entry(e.Seq, NotificationsPage.Kinds.Report, isNew, e.At, who,
                        report.Id == reportRoot.Id ? reportTopic : reportTopic + "/" + Uri.EscapeDataString(report.Id))
                    {
                        Topic = reportRoot.Title ?? "",
                        Tangent = tangents.TryGetValue(reportRoot.TangentId, out var moderators) ? moderators.Name : reportRoot.TangentId,
                        Line = report.Removed ? "" : TangentPage.Opening(report, people),
                        Removed = report.Removed,
                        Reported = reportRoot.OriginId is { } originId && reported.TryGetValue(originId, out var origin) ? Person(people, origin.AuthorId) : null
                    };
                case EventKind.PostCreated or EventKind.Mention when posts.TryGetValue(e.PostId ?? "", out var post)
                                                                      && posts.TryGetValue(e.TopicId ?? "", out var root):
                    var taken = e.Kind == EventKind.PostCreated && post is { ParentId: null, OriginId: not null };
                    var topic = PostWrites.TopicAddress(baseAddress, root.TangentId, root.Slug ?? root.Id);
                    return new NotificationsPage.Entry(e.Seq,
                        e.Kind == EventKind.Mention ? NotificationsPage.Kinds.Mention : taken ? NotificationsPage.Kinds.Taken : NotificationsPage.Kinds.Reply,
                        isNew, e.At, who, post.Id == root.Id ? topic : topic + "/" + Uri.EscapeDataString(post.Id))
                    {
                        Topic = root.Title ?? "",
                        Tangent = tangents.TryGetValue(root.TangentId, out var tangent) ? tangent.Name : root.TangentId,
                        Line = post.Removed ? "" : TangentPage.Opening(post, people),
                        Removed = post.Removed
                    };
                case EventKind.StandingChanged when Readable.Application(e):
                    // Someone applied, and waits for whoever decides: the reader, among them.
                    return new NotificationsPage.Entry(e.Seq, NotificationsPage.Kinds.Application, isNew, e.At, who,
                        baseAddress + ApplicationsController.Path);
                case EventKind.StandingChanged:
                    // Who admitted the reader or lifted their timeout or ban is named, as the door names them; who
                    // declined, timed out or banned them is not.
                    var welcome = e.Standing == Participants.Standing.Admitted;
                    return new NotificationsPage.Entry(e.Seq, NotificationsPage.Kinds.Standing, isNew, e.At, welcome ? who : null, door)
                    {
                        Standing = e.Standing is { } standing ? Participants.People.Word(standing) : null,
                        Was = e.Before is { } before ? Participants.People.Word(before) : null
                    };
                case EventKind.RoleChanged:
                    return new NotificationsPage.Entry(e.Seq, NotificationsPage.Kinds.Role, isNew, e.At, who, profile)
                    {
                        Role = e.Role is { } role ? Roles.Word(role) : null,
                        Given = e.Given
                    };
                case EventKind.OfferMade when offers.TryGetValue(e.OfferId ?? "", out var offer):
                    var open = offer.State == OfferState.Open && answers && offer.DeciderId == reader?.Id;
                    return new NotificationsPage.Entry(e.Seq, NotificationsPage.Kinds.Offer, isNew, e.At, null,
                        open ? null : baseAddress + FirstRunController.Path)
                    {
                        Offer = new NotificationsPage.Offered(offer.Id, Offer.Word(offer.Kind), offer.Subject,
                            offer.State.ToString().ToLowerInvariant(), offer.DecidedAt)
                        {
                            Use = open ? OfferController.UseForm(baseAddress, offer.Id, token!) : null,
                            Decline = open ? OfferController.DeclineForm(baseAddress, offer.Id, token!) : null
                        }
                    };
                default:
                    return new NotificationsPage.Entry(e.Seq, e.Kind, isNew, e.At, who, null);
            }
        }).ToList();
    }

    /// <summary>The Posts the reports among <paramref name="events"/> report, by Id, each only where the reader may view it,
    /// so a report names its author to whoever may view it and to no one else: one keyed read of the Posts the reports'
    /// roots name as their origin, one of their Tangents' rows, and what capability reads there.</summary>
    private static async Task<IReadOnlyDictionary<string, Post>> ReportedBy(IReadOnlyList<Event> events, IReadOnlyDictionary<string, Post> posts,
        string? readerId, CancellationToken ct)
    {
        var ids = events.Where(e => e.Report == true)
            .Select(e => posts.TryGetValue(e.TopicId ?? "", out var root) ? root.OriginId : null)
            .OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Post>();
        var origins = (await Post.Get(ids, ct)).OfType<Post>().ToList();
        var tangentIds = origins.Select(origin => origin.TangentId).Distinct(StringComparer.Ordinal).ToList();
        var tangents = (await Tangents.Tangent.Get(tangentIds, ct)).OfType<Tangents.Tangent>().Select(tangent => tangent.Id).ToList();
        var viewable = await Capabilities.LoadEach(readerId, tangents, ct);
        return origins.Where(origin => viewable.TryGetValue(origin.TangentId, out var capabilities) && capabilities.Exists)
            .ToDictionary(origin => origin.Id, StringComparer.Ordinal);
    }

    /// <summary>Everyone the page shows, in one keyed read, by participant Id, each with their profile's address.</summary>
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
}
