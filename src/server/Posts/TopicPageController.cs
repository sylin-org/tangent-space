using System.Globalization;
using System.Security.Claims;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>The Topic, one resource both ways: its root and one bounded window of its replies, each Post with its
/// author and <c>can</c>, left to the formatter as HTML, a fragment or JSON. At the Topic's address the window opens at
/// a signed-in reader's first unread Post — the first past their checkpoint on the Topic, the newest window when they
/// have read everything — and from the start for a visitor or a reader without one; <see cref="Before"/> and
/// <see cref="After"/> name the window on either side by sequence. At a Post's address the window opens around that
/// Post, marked; at the root's own address, from the start, the root marked. Reading moves no checkpoint: a signed-in
/// reader's page carries the form that moves it through what the page shows (<see cref="CheckpointController"/>). A reader without <see cref="Bit.View"/> on the Tangent receives its gate,
/// and one for whom the Tangent is absent — or an address no Topic or Post of this Topic answers — receives what an
/// address with nothing at it receives. At <c>/t/{tangent}/{tid}</c>, where no Topic has that slug, the root with that TID
/// answers a redirect to its Topic's address. Every read is bounded and keyed: the Tangent; the rows capability is computed
/// from, once for the page; the root by slug; the anchor by Id; at most two ranges of the window index; the reader's
/// checkpoint on the Topic; the people on the page; the participants its bodies mention; where the Topic was taken on a
/// tangent from and the Topics taken from each Post on the page, as far as the reader may view them
/// (<see cref="Origins"/>); and, for a signed-in reader who may not start a Topic in this Tangent, the Tangents where they
/// may, which decide whether each Post offers to take it on a tangent.</summary>
public sealed class TopicPageController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The query parameter naming the sequence the window ends before.</summary>
    public const string Before = "before";

    /// <summary>The query parameter naming the sequence the window starts after.</summary>
    public const string After = "after";

    /// <summary>The segment under a Post's address where it is edited, with its inline remove.</summary>
    public const string EditSegment = "edit";

    /// <summary>The most participants the page resolves: the window's authors first, then who removed its Posts.</summary>
    public const int PeopleLimit = 32;

    [HttpGet("/t/{tangent}/{topic}")]
    [HttpHead("/t/{tangent}/{topic}")]
    public Task<IActionResult> Topic(string tangent, string topic, [FromQuery(Name = Before)] string? before,
        [FromQuery(Name = After)] string? after, CancellationToken ct)
        => Read(tangent, topic, null, before, after, ct);

    [HttpGet("/t/{tangent}/{topic}/{post}")]
    [HttpHead("/t/{tangent}/{topic}/{post}")]
    public Task<IActionResult> AtPost(string tangent, string topic, string post, [FromQuery(Name = Before)] string? before,
        [FromQuery(Name = After)] string? after, CancellationToken ct)
        => Read(tangent, topic, post, before, after, ct);

    private async Task<IActionResult> Read(string tangent, string topic, string? postId, string? before, string? after, CancellationToken ct)
    {
        var personId = User.Identity?.IsAuthenticated == true ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
        // The Tangent's row is read before its overwrites: a Tangent found here committed after its audience was written.
        // A missing Tangent's overwrites are not read, so an address no Tangent has takes no place in Koan's cache.
        var inTangent = await Tangents.Tangent.Get(tangent, ct);
        var rows = await Capabilities.LoadRows(personId, inTangent?.Id, ct);
        var place = rows.For(null);
        if (!place.Exists) return Gated(place.Gate(Bit.View) ?? Capabilities.Absent);
        if (inTangent is not { } tangentRow) return Gated(Capabilities.Absent);
        if (!Slug.IsValid(topic)) return Gated(Capabilities.Absent);
        if (await TopicWindow.Topic(tangentRow.Id, topic, ct) is not { } root)
        {
            // A segment no Topic's slug names, in a TID's shape, is the TID of a Topic's root: it answers a redirect to
            // the Topic's address, so a sender who minted the TID finds the Topic it started without knowing its slug.
            // A slug is read first, so a slug that looks like a TID names its own Topic.
            if (postId is null && Tid.IsValid(topic) && await Post.Get(topic, ct) is { ParentId: null } started && started.TangentId == tangentRow.Id)
                return Redirect(BaseAddress() + "/t/" + Uri.EscapeDataString(tangentRow.Id) + "/" + Uri.EscapeDataString(started.Slug ?? started.Id));
            return Gated(Capabilities.Absent);
        }

        Post? anchor = null;
        if (postId is not null)
        {
            if (!Tid.IsValid(postId) || await Post.Get(postId, ct) is not { } found || found.TopicId != root.Id) return Gated(Capabilities.Absent);
            anchor = found;
        }

        if (before is not null && after is not null || anchor is not null && (before is not null || after is not null))
            return NotAWindow("A window is opened by one of its address's Post, before or after.");
        long? beforeSequence = null, afterSequence = null;
        if (before is not null && (beforeSequence = Sequence(before)) is null)
            return NotAWindow($"The {Before} parameter is the sequence a window of this Topic names as its older.");
        if (after is not null && (afterSequence = Sequence(after)) is null)
            return NotAWindow($"The {After} parameter is the sequence a window of this Topic names as its newer.");

        var reader = rows.Participant;
        var seen = reader is null ? null : await Checkpoint.Get(Checkpoint.Key(reader.Id, Scope.Topic(root.Id)), ct);
        var window = anchor is not null
                ? anchor.ParentId is null ? await TopicWindow.After(root, 0, ct) : await TopicWindow.Around(root, anchor, ct)
            : beforeSequence is { } until ? await TopicWindow.Before(root, until, ct)
            : afterSequence is { } since ? await TopicWindow.After(root, since, ct)
            : seen is { } checkpoint && checkpoint.Seq >= root.LastSequence ? await TopicWindow.Latest(root, ct)
            : await TopicWindow.After(root, seen?.Seq ?? 0, ct);

        var people = await People(window.Replies.Prepend(root).ToList(), ct);
        var baseAddress = BaseAddress();
        var mentioned = await Mentions.Resolve(window.Replies.Prepend(root), baseAddress, ct);
        var taken = await Origins.From(window.Replies.Prepend(root).ToList(), personId, baseAddress, ct);
        var takenFrom = await Origins.Of(root, personId, baseAddress, ct);
        var tangentAddress = baseAddress + "/t/" + Uri.EscapeDataString(tangentRow.Id);
        var address = tangentAddress + "/" + Uri.EscapeDataString(root.Slug ?? root.Id);
        var topicCan = rows.For(root);
        var back = Uri.EscapeDataString(baseUrl.PathBase + "/t/" + tangentRow.Id + "/" + (root.Slug ?? root.Id) + (anchor is null ? "" : "/" + anchor.Id));
        var space = rows.Space;
        var takeOn = reader is not null && (place.Holds(Bit.Topic) || (await Origins.Destinations(personId, ct)).Count > 0);
        // A signed-in reader's page carries their forms with a fresh antiforgery token, so it is never stored or
        // revalidated. The page shows the root and the window's replies through its last, the root alone when it holds
        // none. The seen form is offered only for a window that continues from what the reader has seen — its first
        // reply at most one past their checkpoint, or past the root it shows — so the page never offers to skip a reply
        // it did not show.
        var token = reader is null ? null : FormToken.For(HttpContext);
        var seenSeq = seen?.Seq ?? Checkpoint.Nothing;
        var shownThrough = window.Replies.Count == 0 ? 0 : window.High;
        var continues = window.Low <= Math.Max(seenSeq, 0) + 1;
        if (reader is not null) Response.Headers.CacheControl = "no-cache, no-store";
        return Ok(new TopicPage(
            root.Id,
            root.Slug ?? root.Id,
            root.Title ?? "",
            root.Kind.ToString().ToLowerInvariant(),
            address,
            new TopicPage.InTangent(tangentRow.Id, tangentRow.Name, tangentAddress),
            root.Locked,
            root.Pinned,
            root.ReplyCount,
            topicCan.Can,
            topicCan.Gate(Bit.Post),
            token is not null && continues && shownThrough > seenSeq
                ? new TopicSeen(address + "/" + CheckpointController.SeenSegment, shownThrough, token)
                : null,
            token is null ? null : new Watching(address + "/" + CheckpointController.WatchSegment, seen?.Watch ?? false, token),
            Shown(root, root, address, rows, people, mentioned, seen, reader, taken, takeOn, token),
            window.Replies.Select(reply => Shown(reply, root, address, rows, people, mentioned, seen, reader, taken, takeOn, token)).ToList(),
            window.Replies.Count == 0 ? null : window.Low,
            window.Replies.Count == 0 ? null : window.High,
            window.Older is { } older ? address + "?" + Before + "=" + older.ToString(CultureInfo.InvariantCulture) : null,
            window.Newer is { } newer ? address + "?" + After + "=" + newer.ToString(CultureInfo.InvariantCulture) : null,
            anchor?.Id,
            place.Holds(Bit.Topic) ? tangentAddress + "/" + TangentPageController.NewTopicSegment : null,
            space.Name,
            baseAddress + "/",
            reader is null ? baseAddress + SignInController.Path + "?return=" + back : null,
            reader is null ? null : new TopicPage.Reader(
                reader.Id, Named(reader).Name, Named(reader).Handle, Roles.Word(place.RoleIds), reader.Profile.Avatar,
                baseAddress + AuthConstants.Routes.Logout + "?return=" + back,
                await PostWrites.SettingsFor(reader.Id, baseAddress, ct)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            takenFrom));
    }

    /// <summary>One Post as the page shows it, with what the reader holds on it from the page's rows, where it is
    /// edited where they may edit or remove it, where it is taken on a tangent where <paramref name="takeOn"/> says they
    /// may start a Topic somewhere and it is not removed, the Topics taken from it that they may view, and, with the
    /// page's <paramref name="token"/> for a signed-in reader, the form that reports it: on someone else's Post that is
    /// not removed, outside the moderators' Tangent.</summary>
    private static TopicPage.Post Shown(Post post, Post root, string topicAddress, Capabilities.Rows rows,
        IReadOnlyDictionary<string, TangentPage.Author> people, IReadOnlyDictionary<string, TopicPage.Mention> mentioned, Checkpoint? seen,
        Participant? reader, IReadOnlyDictionary<string, TopicPage.Taken> taken, bool takeOn, FormToken? token)
    {
        var address = topicAddress + "/" + Uri.EscapeDataString(post.Id);
        var can = rows.For(post, root);
        var editable = can.Holds(Bit.EditOwn) || can.Holds(Bit.RemoveOwn) || can.Holds(Bit.Moderate);
        var parent = post.ParentId is { } parentId && parentId != root.Id ? parentId : null;
        return new TopicPage.Post(
            post.Id,
            post.Sequence,
            address,
            Person(people, post.AuthorId),
            post.Removed ? null : post.Body,
            post.Removed ? [] : Mentions.Of(post, mentioned),
            post.CreatedAt,
            post.Version,
            parent,
            parent is null ? null : topicAddress + "/" + Uri.EscapeDataString(parent),
            post.Removed ? new TopicPage.Removal(Person(people, post.RemovedBy ?? post.AuthorId), post.RemovedAt ?? post.CreatedAt) : null,
            reader is not null && (seen is null || post.Sequence > seen.Seq),
            can.Can,
            editable ? address + "/" + EditSegment : null,
            takeOn && !post.Removed ? address + "/" + TakeOnATangentController.Segment : null,
            taken.GetValueOrDefault(post.Id),
            token is not null && !post.Removed && post.AuthorId != reader?.Id && post.TangentId != rows.Space.ModeratorsTangentId
                ? new TopicPage.Report(address + "/" + ReportController.Segment, Tid.Mint(DateTimeOffset.UtcNow), token) : null);
    }

    /// <summary>A cursor's sequence: a non-negative whole number; null when the text is not one.</summary>
    private static long? Sequence(string text)
        => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) ? sequence : null;

    private static IActionResult NotAWindow(string detail)
        => new Problem(StatusCodes.Status400BadRequest, "Not a window of this Topic", detail).ToResult();

    /// <summary>What a reader without <see cref="Bit.View"/> receives: a missing resource's answer where the Tangent
    /// is absent to them, the same for every address under <c>/t/</c> whether anything is there or not; otherwise the
    /// one step that stands between them and reading.</summary>
    private static IActionResult Gated(string gate) => gate switch
    {
        Capabilities.Absent => new Problem(StatusCodes.Status404NotFound, "Not found", "Nothing answers at this address.", gate).ToResult(),
        Capabilities.SignIn => new Problem(StatusCodes.Status401Unauthorized, "Sign in to read",
            "This Tangent Space is read by its participants.", gate).ToResult(),
        Capabilities.Apply => new Problem(StatusCodes.Status403Forbidden, "Waiting to be admitted",
            "Your application is waiting: you can read here once someone admits you.", gate).ToResult(),
        Capabilities.Declined => new Problem(StatusCodes.Status403Forbidden, "Not admitted",
            "Your application was declined, so this Tangent Space is not open to you.", gate).ToResult(),
        _ => new Problem(StatusCodes.Status403Forbidden, "Not readable", "This Tangent is not open to you.", gate).ToResult()
    };

    private static TangentPage.Author Person(IReadOnlyDictionary<string, TangentPage.Author> people, string id)
        => people.TryGetValue(id, out var person) ? person : new TangentPage.Author(id, id, id, "person", null);

    /// <summary>The authors of these Posts, then who removed any of them, at most <see cref="PeopleLimit"/>, in one keyed
    /// read, by participant Id.</summary>
    private static async Task<IReadOnlyDictionary<string, TangentPage.Author>> People(IReadOnlyList<Post> posts, CancellationToken ct)
    {
        var ids = posts.Select(post => post.AuthorId)
            .Concat(posts.Where(post => post.Removed).Select(post => post.RemovedBy).OfType<string>())
            .Distinct(StringComparer.Ordinal).Take(PeopleLimit).ToList();
        var found = await Participant.Get(ids, ct);
        return found.OfType<Participant>().ToDictionary(row => row.Id, row =>
        {
            var (name, handle) = Named(row);
            return new TangentPage.Author(row.Id, handle, name, row.Kind.ToString().ToLowerInvariant(), row.Profile.Avatar);
        }, StringComparer.Ordinal);
    }

    /// <summary>The name a participant is shown by, and their handle: the profile's display name, else the handle,
    /// else their Id.</summary>
    private static (string Name, string Handle) Named(Participant participant)
    {
        var handle = participant.Handle.Length > 0 ? participant.Handle : participant.Id;
        return (participant.Profile.DisplayName is { Length: > 0 } name ? name : handle, handle);
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
