using System.ComponentModel;
using System.Security.Claims;
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

/// <summary>The Tangent page, one resource both ways: the Tangent, its <c>can</c>, and one bounded page of its Topics
/// with what is new to the reader marked, left to the formatter as HTML, a fragment or JSON. It needs
/// <see cref="Bit.View"/>, which the Doorway judges before anything else (<see cref="Access.Doorway"/>), so a reader without it
/// receives its gate, and one for whom the Tangent is absent what an address with no Tangent receives. Every read is bounded
/// and keyed: the Tangent and capability's rows, which the Doorway read, one read of Topics, their latest replies by Id, the people on the page, the reader's place mark and their marks on
/// those Topics, which say what is new to them (<see cref="Unread"/>), the first <see cref="SwitcherLimit"/>
/// Tangents the reader may view for the switcher (<see cref="ViewableTangents"/>), and, for a signed-in reader, what they
/// hold in the place, which says whether the switcher offers a new Tangent.</summary>
public sealed class TangentPageController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    /// <summary>The segment under a Tangent's address where a Topic is started; <see cref="Slug.Reserved"/> keeps it
    /// from every Topic.</summary>
    public const string NewTopicSegment = "new";

    /// <summary>The most Tangents the switcher lists; the rest are a link away (<see cref="TangentsController"/>).</summary>
    public const int SwitcherLimit = 50;

    [HttpGet("/t/{tangent}")]
    [HttpHead("/t/{tangent}")]
    [Tool(Name = "read_tangent")]
    [Needs(Bit.View)]
    [Description("Read a Tangent: its Topics a page at a time, what is new to you marked. Follow with read_topic on a Topic's slug, or start_topic to begin one.")]
    public async Task<IActionResult> Get([Description(Holds.Tangent)] string tangent, [FromQuery(Name = After), Description(Holds.After)] string? after, CancellationToken ct)
    {
        var here = Here.Of(HttpContext);
        var personId = here.PersonId;
        var row = here.Tangent!;
        var capabilities = here.Capabilities;

        TopicList.Cursor? cursor = null;
        if (after is not null && (cursor = TopicList.Cursor.Parse(after)) is null)
            return new Problem(StatusCodes.Status400BadRequest,
                $"The {After} parameter is the cursor a page of this Tangent names as its next.") { Title = "Not a page of this Tangent" }.ToResult();

        var page = await TopicList.Read(row.Id, cursor, ct);
        var reader = here.Rows.Participant;
        var latest = await LatestReplies(page.Topics, ct);
        var people = await People(page.Topics, latest.Values, ct);
        var unread = reader is null ? null : await Unread.Of(reader, ct);
        var marks = unread is null ? new Dictionary<string, long>() : await unread.Marks(page.Topics.Select(topic => topic.Id), ct);
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
            page.Topics.Select(topic => Topic(topic, address, people, latest, unread, marks)).ToList(),
            page.Next is { } next ? address + "?" + After + "=" + Uri.EscapeDataString(next.ToString()) : null,
            cursor is not null,
            capabilities.Holds(Bit.Topic) ? address + "/" + NewTopicSegment : null,
            reader is null ? baseAddress + SignInController.Path + "?return=" + back : null,
            await Viewable(personId, row, ct),
            space.Name,
            baseAddress + "/",
            reader is null ? null : new TangentPage.Reader(
                reader.Id, reader.ShownName(), reader.ShownHandle(), Role(capabilities), reader.Profile.Avatar,
                baseAddress + AuthConstants.Routes.Logout + "?return=" + back, place is null ? null : TangentSpaceSettingsController.Entry(place, baseAddress),
                await unread!.BadgeNow(baseAddress + "/", ct)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            capabilities.Holds(Bit.ManageTangent) ? address + "/" + TangentWrites.SettingsSegment : null,
            creates ? baseAddress + NewTangentController.Route : null,
            audience == Audience.Public ? Feeds.TangentLinks(baseAddress, row.Id, row.Name, space.Name) : null,
            reader is not null && Inside.In(await Inside.Own(row.Id, reader.Id, ct))
                ? Render.Form.Posting(address + "/" + JoinController.LeaveSegment)
                : null));
    }

    /// <summary>One Topic as a list shows it, what is new in it marked for a signed-in reader by the one rule
    /// (<see cref="Unread.InTopic"/>), and none for a visitor.</summary>
    public static TangentPage.Topic Topic(Post root, string tangentAddress, IReadOnlyDictionary<string, TangentPage.Author> people,
        IReadOnlyDictionary<string, Post> latest, Unread? unread, IReadOnlyDictionary<string, long> marks)
    {
        long? mark = marks.TryGetValue(root.Id, out var read) ? read : null;
        var (isNew, newPosts) = unread?.InTopic(root, mark) ?? (false, 0);
        var address = tangentAddress + "/" + Uri.EscapeDataString(root.Slug ?? root.Id);
        TangentPage.Latest? reply = null;
        if (root.LatestReplyId is { } replyId && latest.TryGetValue(replyId, out var post))
            reply = new TangentPage.Latest(post.Id, Person(people, post.AuthorId), TangentPage.Opening(post, people), post.CreatedAt,
                unread is not null && unread.New(post, mark), TangentPage.EditedOf(post, people));
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
            reply,
            root.RecentAuthorIds.Take(Post.RecentAuthorLimit).Select(id => Person(people, id)).ToList(),
            isNew,
            newPosts,
            TangentPage.EditedOf(root, people),
            root.CoverId is { } cover ? tangentAddress[..tangentAddress.LastIndexOf("/t/", StringComparison.Ordinal)] + Media.Reference(cover) : null);
    }

    private static TangentPage.Author Person(IReadOnlyDictionary<string, TangentPage.Author> people, string id)
        => people.TryGetValue(id, out var person) ? person : new TangentPage.Author(id, id, id, "person", null);

    /// <summary>The latest reply of each Topic on the page that has one, by its Id, in one keyed read.</summary>
    public static async Task<IReadOnlyDictionary<string, Post>> LatestReplies(IReadOnlyList<Post> topics, CancellationToken ct)
    {
        var ids = topics.Select(topic => topic.LatestReplyId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Post>();
        return (await Post.Get(ids, ct)).OfType<Post>().ToDictionary(post => post.Id, StringComparer.Ordinal);
    }

    /// <summary>Everyone the page shows (the Topics' authors, their recent authors, the authors of their latest
    /// replies, whom those openings and replies mention, and who else edited them) in one keyed read, by participant Id.</summary>
    public static async Task<IReadOnlyDictionary<string, TangentPage.Author>> People(IReadOnlyList<Post> topics, IEnumerable<Post> latest,
        CancellationToken ct)
    {
        var ids = topics.SelectMany(topic => topic.RecentAuthorIds.Take(Post.RecentAuthorLimit).Append(topic.AuthorId))
            .Concat(latest.Select(post => post.AuthorId))
            .Concat(topics.Concat(latest).SelectMany(post => post.MentionedIds))
            .Concat(topics.Concat(latest).Select(post => post.EditedBy).OfType<string>()).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, TangentPage.Author>();
        var rows = await Participant.Get(ids, ct);
        return rows.OfType<Participant>().ToDictionary(row => row.Id, row => TangentPage.Author.Of(row), StringComparer.Ordinal);
    }


    /// <summary>The highest seeded role the reader holds, in words; null for none.</summary>
    private static string? Role(Capabilities capabilities) => Roles.Word(capabilities.RoleIds);

    /// <summary>The first <see cref="SwitcherLimit"/> Tangents the reader may view, this one always among them.</summary>
    private async Task<IReadOnlyList<TangentPage.Listed>> Viewable(string? personId, Tangent current, CancellationToken ct)
    {
        var listed = (await ViewableTangents.Read(personId, ViewableTangents.Views, null, SwitcherLimit, ct)).Tangents.ToList();
        if (listed.All(row => row.Id != current.Id)) listed.Add(current);
        return listed.Select(row => new TangentPage.Listed(row.Id, row.Name, Word(row.DefaultKind), Address(row.Id),
            row.Id == current.Id ? current.TopicCount : row.TopicCount, Tile: Tile.For(row, BaseAddress() + "/"))).ToList();
    }

    private string Address(string slug) => BaseAddress() + "/t/" + Uri.EscapeDataString(slug);

    private static string Word(PostKind kind) => kind.ToString().ToLowerInvariant();

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
