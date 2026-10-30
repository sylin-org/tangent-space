using System.Security.Claims;
using Koan.Data.Abstractions;
using Koan.Data.Core.Sorting;
using Koan.Identity.Roles;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>The Tangent page, one resource both ways: the Tangent, its <c>can</c>, and one bounded page of its Topics
/// with what is new to the reader marked, left to the formatter as HTML, a fragment or JSON. A reader without
/// <see cref="Bit.View"/> on the Tangent receives its gate instead, and one for whom the Tangent is absent receives
/// what an address with no Tangent receives. Every read is bounded and keyed: the Tangent, then capability's rows, at
/// most two reads of Topics, their latest replies by Id, the people on the page, the reader's checkpoints for those
/// Topics and for the Tangent, whose watch flag the page's watch form shows, the first <see cref="SwitcherLimit"/> Tangents by order with their overwrites for the switcher, and, for a
/// signed-in reader, what they hold in the place, which says whether the switcher offers a new Tangent.</summary>
public sealed class TangentPageController(KoanWebBaseUrl baseUrl, RoleCollection roles) : ControllerBase
{
    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    /// <summary>The segment under a Tangent's address where a Topic is started; <see cref="Slug.Reserved"/> keeps it
    /// from every Topic.</summary>
    public const string NewTopicSegment = "new";

    /// <summary>The most Tangents the switcher reads, by order.</summary>
    public const int SwitcherLimit = 50;

    [HttpGet("/t/{tangent}")]
    [HttpHead("/t/{tangent}")]
    public async Task<IActionResult> Get(string tangent, [FromQuery(Name = After)] string? after, CancellationToken ct)
    {
        var personId = User.Identity?.IsAuthenticated == true ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
        // The Tangent's row is read before its overwrites: a Tangent found here committed after its audience was written.
        // A missing Tangent's overwrites are not read, so an address no Tangent has takes no place in Koan's cache.
        var found = await Tangent.Get(tangent, ct);
        var capabilities = await Capabilities.Load(personId, found?.Id, null, ct);
        if (!capabilities.Exists) return Gated(capabilities.Gate(Bit.View) ?? Capabilities.Absent);
        if (found is not { } row) return Gated(Capabilities.Absent);

        TopicList.Cursor? cursor = null;
        if (after is not null && (cursor = TopicList.Cursor.Parse(after)) is null)
            return new Problem(StatusCodes.Status400BadRequest, "Not a page of this Tangent",
                $"The {After} parameter is the cursor a page of this Tangent names as its next.").ToResult();

        var page = await TopicList.Read(row.Id, cursor, ct);
        var reader = personId is null ? null : await Participant.Get(personId, ct);
        var latest = await LatestReplies(page.Topics, ct);
        var people = await People(page.Topics, latest.Values, ct);
        var seen = await Seen(reader, page.Topics, ct);
        var watched = reader is not null && (await Checkpoint.Get(Checkpoint.Key(reader.Id, Scope.Tangent(row.Id)), ct))?.Watch == true;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        var audience = space.Audience == Audience.Private || await Overwrites.AudienceOf(roles, row.Id, ct) == Audience.Private
            ? Audience.Private
            : Audience.Public;

        var configured = Configured();
        var address = Address(row.Id);
        var back = Uri.EscapeDataString(baseUrl.PathBase + "/t/" + row.Id);
        // A signed-in reader's page carries the watch form with a fresh antiforgery token, so it is never stored or
        // revalidated.
        var token = reader is null ? null : FormToken.For(HttpContext);
        if (reader is not null) Response.Headers.CacheControl = "no-cache, no-store";
        var creates = reader is not null && (await Capabilities.Load(personId, null, null, ct)).Holds(Bit.ManageSpace);
        return Ok(new TangentPage(
            row.Id,
            row.Name,
            row.Description,
            Word(row.DefaultKind),
            audience.ToString().ToLowerInvariant(),
            address,
            capabilities.Can,
            page.Topics.Select(topic => Topic(topic, address, people, latest, seen, reader is not null)).ToList(),
            page.Next is { } next ? address + "?" + After + "=" + Uri.EscapeDataString(next.ToString()) : null,
            cursor is not null,
            capabilities.Holds(Bit.Topic) ? address + "/" + NewTopicSegment : null,
            token is null ? null : new Watching(address + "/" + CheckpointController.WatchSegment, watched, token),
            reader is null ? configured + SignInController.Path + "?return=" + back : null,
            await Viewable(personId, row, ct),
            space.Name,
            configured + "/",
            reader is null ? null : new TangentPage.Reader(
                reader.Id, Named(reader).Name, Named(reader).Handle, Role(capabilities), reader.Profile.Avatar,
                configured + AuthConstants.Routes.Logout + "?return=" + back),
            DateOnly.FromDateTime(DateTime.UtcNow),
            TangentWrites.MayManage(capabilities) ? address + "/" + TangentWrites.SettingsSegment : null,
            creates ? configured + NewTangentController.Route : null));
    }

    /// <summary>What a reader without <see cref="Bit.View"/> receives: a missing resource's answer where the Tangent
    /// is absent to them, the same for every address under <c>/t/</c> whether a Tangent is there or not; otherwise
    /// the one step that stands between them and reading.</summary>
    private static IActionResult Gated(string gate) => gate switch
    {
        Capabilities.Absent => new Problem(StatusCodes.Status404NotFound, "Not found", "Nothing answers at this address.", gate).ToResult(),
        Capabilities.SignIn => new Problem(StatusCodes.Status401Unauthorized, "Sign in to read",
            "This Tangent Space is read by its participants.", gate).ToResult(),
        Capabilities.Apply => new Problem(StatusCodes.Status403Forbidden, "Waiting to be admitted",
            "The proprietor admits new participants; you can read once you are admitted.", gate).ToResult(),
        _ => new Problem(StatusCodes.Status403Forbidden, "Not readable", "This Tangent is not open to you.", gate).ToResult()
    };

    private static TangentPage.Topic Topic(Post root, string tangentAddress, IReadOnlyDictionary<string, TangentPage.Author> people,
        IReadOnlyDictionary<string, Post> latest, IReadOnlyDictionary<string, long> seen, bool signedIn)
    {
        var known = seen.TryGetValue(root.Id, out var last);
        var unseen = !signedIn ? 0 : known ? Math.Max(0, root.LastSequence - last) : root.LastSequence + 1;
        var address = tangentAddress + "/" + Uri.EscapeDataString(root.Slug ?? root.Id);
        TangentPage.Latest? reply = null;
        if (root.LatestReplyId is { } replyId && latest.TryGetValue(replyId, out var post))
            reply = new TangentPage.Latest(post.Id, Person(people, post.AuthorId), TangentPage.Opening(post, people), post.CreatedAt,
                signedIn && (!known || post.Sequence > last));
        return new TangentPage.Topic(
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
            root.Pinned,
            root.Locked,
            reply,
            root.RecentAuthorIds.Take(Post.RecentAuthorLimit).Select(id => Person(people, id)).ToList(),
            unseen > 0,
            unseen);
    }

    private static TangentPage.Author Person(IReadOnlyDictionary<string, TangentPage.Author> people, string id)
        => people.TryGetValue(id, out var person) ? person : new TangentPage.Author(id, id, id, "person", null);

    /// <summary>The latest reply of each Topic on the page that has one, by its Id, in one keyed read.</summary>
    private static async Task<IReadOnlyDictionary<string, Post>> LatestReplies(IReadOnlyList<Post> topics, CancellationToken ct)
    {
        var ids = topics.Select(topic => topic.LatestReplyId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Post>();
        return (await Post.Get(ids, ct)).OfType<Post>().ToDictionary(post => post.Id, StringComparer.Ordinal);
    }

    /// <summary>Everyone the page shows (the Topics' authors, their recent authors, the authors of their latest
    /// replies, and whom those openings and replies mention) in one keyed read, by participant Id.</summary>
    private static async Task<IReadOnlyDictionary<string, TangentPage.Author>> People(IReadOnlyList<Post> topics, IEnumerable<Post> latest,
        CancellationToken ct)
    {
        var ids = topics.SelectMany(topic => topic.RecentAuthorIds.Take(Post.RecentAuthorLimit).Append(topic.AuthorId))
            .Concat(latest.Select(post => post.AuthorId))
            .Concat(topics.Concat(latest).SelectMany(post => post.MentionedIds)).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, TangentPage.Author>();
        var rows = await Participant.Get(ids, ct);
        return rows.OfType<Participant>().ToDictionary(row => row.Id, row =>
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

    /// <summary>The highest seeded role the reader holds, in words; null for none.</summary>
    private static string? Role(Capabilities capabilities) => Roles.Word(capabilities.RoleIds);

    /// <summary>The Tangents the reader may view among the first <see cref="SwitcherLimit"/> by order, this one always
    /// among them: one read of the Tangents and one of their overwrites.</summary>
    private async Task<IReadOnlyList<TangentPage.Listed>> Viewable(string? personId, Tangent current, CancellationToken ct)
    {
        var rows = await Tangent.All(new QueryDefinition { Page = 1, PageSize = SwitcherLimit }
            .WithSort<Tangent>(sort => sort.OrderBy(tangent => tangent.Order).ThenBy(tangent => tangent.Id)), ct);
        var each = await Capabilities.LoadEach(personId, rows.Select(row => row.Id).ToList(), ct);
        var listed = rows.Where(row => row.Id == current.Id || each[row.Id].Exists).ToList();
        if (listed.All(row => row.Id != current.Id)) listed.Add(current);
        return listed.Select(row => new TangentPage.Listed(row.Id, row.Name, Word(row.DefaultKind), Address(row.Id),
            row.Id == current.Id ? current.TopicCount : row.TopicCount)).ToList();
    }

    private string Address(string slug) => Configured() + "/t/" + Uri.EscapeDataString(slug);

    /// <summary>The sequence the reader last saw in each of the page's Topics, by Topic Id, in one keyed read of their
    /// checkpoints; empty for a visitor. A Topic without a checkpoint is absent from the answer.</summary>
    private static async Task<IReadOnlyDictionary<string, long>> Seen(Participant? reader, IReadOnlyList<Post> topics, CancellationToken ct)
    {
        if (reader is null || topics.Count == 0) return new Dictionary<string, long>();
        var checkpoints = await Checkpoint.Get(topics.Select(topic => Checkpoint.Key(reader.Id, Scope.Topic(topic.Id))).ToList(), ct);
        var seen = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var index = 0; index < topics.Count; index++)
        {
            if (checkpoints[index] is { } checkpoint) seen[topics[index].Id] = checkpoint.Seq;
        }

        return seen;
    }

    private static string Word(PostKind kind) => kind.ToString().ToLowerInvariant();

    private string Configured() => baseUrl.Configured
        ?? throw new InvalidOperationException("Koan:Web:ApplicationUrl must be set to the base URL the Tangent Space answers at.");
}
