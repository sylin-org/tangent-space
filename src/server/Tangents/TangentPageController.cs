using System.Security.Claims;
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
/// what an address with no Tangent receives. Every read is bounded and keyed: capability's rows, the Tangent, at most
/// two reads of Topics, the authors on the page, and the reader's checkpoints for those Topics.</summary>
public sealed class TangentPageController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    /// <summary>The segment under a Tangent's address where a Topic is started.</summary>
    public const string NewTopicSegment = "new";

    [HttpGet("/t/{tangent}")]
    [HttpHead("/t/{tangent}")]
    public async Task<IActionResult> Get(string tangent, [FromQuery(Name = After)] string? after, CancellationToken ct)
    {
        var personId = User.Identity?.IsAuthenticated == true ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
        var capabilities = await Capabilities.Load(personId, tangent, null, ct);
        if (!capabilities.Exists) return Gated(capabilities.Gate(Bit.View) ?? Capabilities.Absent);
        if (await Tangent.Get(tangent, ct) is not { } row) return Gated(Capabilities.Absent);

        TopicList.Cursor? cursor = null;
        if (after is not null && (cursor = TopicList.Cursor.Parse(after)) is null)
            return new Problem(StatusCodes.Status400BadRequest, "Not a page of this Tangent",
                $"The {After} parameter is the cursor a page of this Tangent names as its next.").ToResult();

        var page = await TopicList.Read(row.Id, cursor, ct);
        var reader = personId is null ? null : await Participant.Get(personId, ct);
        var authors = await Authors(page.Topics, ct);
        var seen = await Seen(reader, page.Topics, ct);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        var everyone = await Overwrite.Get(Overwrite.Key(row.Id, Overwrite.Everyone), ct);
        var audience = space.Audience == Audience.Private || everyone?.AudienceStated() == Audience.Private
            ? Audience.Private
            : Audience.Public;

        var configured = Configured();
        var address = configured + "/t/" + Uri.EscapeDataString(row.Id);
        if (reader is not null) Response.Headers.CacheControl = "private";
        return Ok(new TangentPage(
            row.Id,
            row.Name,
            row.Description,
            Word(row.DefaultKind),
            audience.ToString().ToLowerInvariant(),
            address,
            capabilities.Can,
            page.Topics.Select(topic => Topic(topic, address, authors, seen, reader is not null)).ToList(),
            page.Next is { } next ? address + "?" + After + "=" + Uri.EscapeDataString(next.ToString()) : null,
            cursor is not null,
            capabilities.Holds(Bit.Topic) ? address + "/" + NewTopicSegment : null,
            capabilities.Gate(Bit.Topic) == Capabilities.SignIn
                ? configured + SignInController.Path + "?return=" + Uri.EscapeDataString(baseUrl.PathBase + "/t/" + row.Id)
                : null,
            space.Name,
            configured + "/",
            reader is null ? null : new TangentPage.Reader(reader.Handle.Length > 0 ? reader.Handle : reader.Id, reader.Profile.Avatar,
                configured + SignInController.SignOutPath + "?return=" + Uri.EscapeDataString(baseUrl.PathBase + "/t/" + row.Id))));
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

    private static TangentPage.Topic Topic(Post root, string tangentAddress, IReadOnlyDictionary<string, TangentPage.Author> authors,
        IReadOnlyDictionary<string, long> seen, bool signedIn)
    {
        var unseen = !signedIn ? 0
            : seen.TryGetValue(root.Id, out var last) ? Math.Max(0, root.LastSequence - last)
            : root.LastSequence + 1;
        return new TangentPage.Topic(
            root.Id,
            root.Slug ?? root.Id,
            root.Title ?? "",
            Word(root.Kind),
            tangentAddress + "/" + Uri.EscapeDataString(root.Slug ?? root.Id),
            authors.TryGetValue(root.AuthorId, out var author) ? author : new TangentPage.Author(root.AuthorId, root.AuthorId, root.AuthorId, "person", null),
            TangentPage.Opening(root.Body),
            root.ReplyCount,
            root.CreatedAt,
            root.LastActivityAt,
            root.Pinned,
            root.Locked,
            unseen > 0,
            unseen);
    }

    /// <summary>The authors of the page's Topics, in one keyed read.</summary>
    private static async Task<IReadOnlyDictionary<string, TangentPage.Author>> Authors(IReadOnlyList<Post> topics, CancellationToken ct)
    {
        var ids = topics.Select(topic => topic.AuthorId).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, TangentPage.Author>();
        var rows = await Participant.Get(ids, ct);
        return rows.Where(row => row is not null).ToDictionary(row => row!.Id, row =>
        {
            var handle = row!.Handle.Length > 0 ? row.Handle : row.Id;
            return new TangentPage.Author(row.Id, handle, row.Profile.DisplayName is { Length: > 0 } name ? name : handle,
                row.Kind.ToString().ToLowerInvariant(), row.Profile.Avatar);
        }, StringComparer.Ordinal);
    }

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
