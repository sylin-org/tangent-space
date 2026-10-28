using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Activity;
using Tangent.Application;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Identity;
using Tangent.Infrastructure;

namespace Tangent.Api;

/// <summary>Personal workspace views apply current resource policy on every read.</summary>
[ApiController, Authorize, Route("api/workspace")]
public sealed class WorkspaceController(TangentServer hub, ExperienceDigest digest, References refs,
    TimeProvider clock, PolicyGate gate, ActivityService activity) : ControllerBase
{
    private string Actor() => ParticipationAccess.Require(User, ParticipationGrants.Read);
    private static readonly QueryDefinition Watches = new() { Sort = SortSpecParser.ParseStrict<WatchSetting>("-ChangedAt,-Id") };

    [HttpGet("preferences")]
    public Task<IActionResult> Preferences(CancellationToken ct) => Execute(async actor =>
        Ok(await ParticipantPreferences.Get(actor, ct) ?? new ParticipantPreferences { Id = actor }));

    [TopicMutation(ParticipationGrants.Read), HttpPatch("preferences")]
    public Task<IActionResult> ChangePreferences(PreferencePatch patch, CancellationToken ct) => ExecuteWrite(async actor =>
    {
        if (patch.Theme is not null && patch.Theme is not ("system" or "light" or "dark"))
            throw new ArgumentException("Choose System, Light or Dark.");
        var preferences = await ParticipantPreferences.Get(actor, ct) ?? new ParticipantPreferences { Id = actor };
        if (patch.Theme is not null) preferences.Theme = patch.Theme;
        if (patch.NotifyReplies is { } replies) preferences.NotifyReplies = replies;
        if (patch.NotifyMentions is { } mentions) preferences.NotifyMentions = mentions;
        if (patch.NotifyFollowing is { } following) preferences.NotifyFollowing = following;
        if (patch.NotifyNewPeople is { } people) preferences.NotifyNewPeople = people;
        if (patch.RepliesPlacement is not null) { if(patch.RepliesPlacement is not ("beside" or "below"))throw new ArgumentException("Choose Beside or Below."); preferences.RepliesPlacement=patch.RepliesPlacement; }
        await preferences.Save(ct);
        return Ok(preferences);
    });

    [HttpGet("reading")]
    public Task<IActionResult> Reading([FromQuery] string topic, CancellationToken ct) => Execute(async actor => {
        if ((await hub.Topics.Describe(actor, topic, ct))?.CanRead != true) return NotFound();
        return Ok(new { sequence = (await ReadPosition.Get(ReadPosition.Key(actor, topic), ct))?.Sequence ?? 0 });
    });

    [HttpGet("follow")]
    public Task<IActionResult> Follow([FromQuery] string tangent, [FromQuery] string? topic, CancellationToken ct)
        => Execute(async actor =>
        {
            if (topic is not null)
            {
                var description = await hub.Topics.Describe(actor, topic, ct);
                if (description?.CanRead != true || description.TangentKey != tangent) return NotFound();
                var watch = await WatchSetting.Get(WatchSetting.Key(actor, topic), ct);
                return Ok(new { following = watch is { Mode: not WatchMode.None }, scopeRef = refs.Topic(tangent, topic) });
            }
            if (await hub.Tangents.Describe(actor, tangent, ct) is null) return NotFound();
            var tangentWatch = await TangentWatchSetting.Get(TangentWatchSetting.Key(actor, tangent), ct);
            return Ok(new { following = tangentWatch is { Mode: not WatchMode.None }, scopeRef = refs.Tangent(tangent) });
        });

    [HttpGet("following")]
    public Task<IActionResult> Following([FromQuery] int page = 1, CancellationToken ct = default)
        => Execute(async actor =>
        {
            if (page is < 1 or > 10000) throw new ArgumentException("Choose a valid page.");
            var candidates = await WatchSetting.Query(row => row.ParticipantId == actor && row.Mode != WatchMode.None,
                Watches.WithPagination(page, 25), ct);
            var items = new List<object>();
            foreach (var watch in candidates.Take(25))
            {
                var topic = await hub.Topics.Describe(actor, watch.TopicKey, ct);
                if (topic?.CanRead != true) continue;
                items.Add(new { topic.Key, topic.TangentKey, topic.Title, topic.Presentation,
                    url = $"/t/{Uri.EscapeDataString(topic.TangentKey)}/topics/{Uri.EscapeDataString(topic.Key)}" });
            }
            var tangentCandidates=await TangentWatchSetting.Query(row=>row.ParticipantId==actor && row.Mode!=WatchMode.None,
                new QueryDefinition {Sort=SortSpecParser.ParseStrict<TangentWatchSetting>("-ChangedAt,-Id")}.WithPagination(page,25),ct);
            foreach(var watch in tangentCandidates) {var tangent=await hub.Tangents.Describe(actor,watch.TangentKey,ct);if(tangent is null)continue;items.Add(new {key=tangent.Key,tangentKey=tangent.Key,title=tangent.Name,presentation="tangent",url=$"/t/{Uri.EscapeDataString(tangent.Key)}/topics"});}
            return Ok(new { items, nextPage = candidates.Count == 25 || tangentCandidates.Count==25 ? (int?)(page + 1) : null });
        });

    [HttpGet("notifications")]
    public Task<IActionResult> Notifications([FromQuery] string? cursor, CancellationToken ct) => Execute(async actor =>
    {
        var preferences = await ParticipantPreferences.Get(actor, ct) ?? new ParticipantPreferences { Id = actor };
        var space = await Tangent.Spaces.Space.Get(TangentConstants.SpaceId, ct);
        var withArrivals = preferences.NotifyNewPeople && space?.IsOwner(actor) == true;
        // Reserve five places for admission requests; the complete response stays within 25.
        var postLimit = withArrivals ? 20 : 25;
        var filterKey = $"notifications:{withArrivals}:{preferences.NotifyReplies}:{preferences.NotifyMentions}:{preferences.NotifyFollowing}:{preferences.NotifyNewPeople}";
        var decoded = digest.Decode(cursor, actor, null, null, filterKey);
        if (!string.IsNullOrEmpty(cursor) && decoded is null)
            throw new ArgumentException("This notification page has expired or your settings changed. Return to the first page.");
        var offset = decoded?.Offset ?? 0;
        var attention = await digest.Page(actor, User.FindFirst(ParticipationConstants.CredentialClaim)?.Value,
            decoded, null, null, postLimit, ct,
            new(preferences.NotifyReplies, preferences.NotifyMentions, preferences.NotifyFollowing));
        var items = new List<object>();
        foreach (var item in attention.Items)
        {
            var source = refs.ParsePost(item.SourceRef);
            if (source is null) continue;
            var topic = await hub.Topics.Describe(actor, source.Value.TopicKey, ct);
            if (topic?.CanRead != true || topic.TangentKey != source.Value.TangentKey) continue;
            var post = await Post.Get(source.Value.PostId, ct);
            if (post is null || post.Removed || post.TopicKey != topic.Key || post.AuthorParticipantId == actor) continue;
            var participant = await Participant.Get(post.AuthorParticipantId, ct);
            var profile = await hub.Profiles.Read(post.AuthorParticipantId, ct);
            items.Add(new { id = post.Id, kind = item.Kind, title = topic.Title, topicKey = topic.Key,
                tangentKey = topic.TangentKey, excerpt = item.Excerpt, createdAt = post.AcceptedAt,
                unread = post.AcceptedAt > preferences.NotificationsReadAt,
                author = new { id = post.AuthorParticipantId, displayName = profile.DisplayName ?? item.ActorName,
                    handle = profile.Handle, avatar = profile.Avatar, classification = participant?.Classification.ToString() },
                url = $"/t/{Uri.EscapeDataString(topic.TangentKey)}/{Uri.EscapeDataString(post.Id)}" });
        }
        var arrivalsMore = false;
        async Task<IReadOnlyList<Tangent.Spaces.SpaceMembership>> ArrivalsAt(int page)
            => await Tangent.Spaces.SpaceMembership.Query(row => row.Status == "pending",
                new QueryDefinition { Sort = SortSpecParser.ParseStrict<Tangent.Spaces.SpaceMembership>("-RequestedAt,-Id") }
                    .WithPagination(page, 5), ct);
        static string ArrivalSelection(IReadOnlyList<Tangent.Spaces.SpaceMembership> rows)
            => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(rows))));
        var pageNumber = offset / postLimit + 1;
        string? nextArrivals = null;
        if (withArrivals)
        {
            var arrivals = await ArrivalsAt(pageNumber);
            if (decoded?.Arrivals is { } expected && ArrivalSelection(arrivals) != expected)
                throw new ArgumentException("Admission requests changed. Return to the first notification page.");
            var next = await ArrivalsAt(pageNumber + 1);
            arrivalsMore = next.Count > 0;
            nextArrivals = ArrivalSelection(next);
            foreach (var arrival in arrivals)
            {
                var profile = await hub.Profiles.Read(arrival.Id, ct);
                var participant = await Participant.Get(arrival.Id, ct);
                items.Add(new { id = "arrival-" + arrival.Id, kind = "new_people", title = "Space", topicKey = "", tangentKey = "",
                    excerpt = "Asked to join the Space.", createdAt = arrival.RequestedAt,
                    unread = arrival.RequestedAt > preferences.NotificationsReadAt,
                    author = new { id = arrival.Id, displayName = profile.DisplayName ?? profile.Handle ?? "Participant",
                        profile.Handle, profile.Avatar, classification = participant?.Classification.ToString() }, url = "/settings?tab=people" });
            }
        }
        var more = attention.More || arrivalsMore;
        string CursorAt(int position, bool continuation) => digest.Encode(new(actor, null, null, position, clock.GetUtcNow().AddHours(1), filterKey,
            continuation ? attention.Selection : null, continuation ? nextArrivals : null));
        return Ok(new { items, more, incomplete = attention.Incomplete,
            nextCursor = more ? CursorAt(offset + postLimit, true) : null,
            previousCursor = offset > 0 ? CursorAt(0, false) : null });
    });

    [TopicMutation(ParticipationGrants.Read), HttpPost("notifications/read")]
    public Task<IActionResult> ReadNotifications([FromQuery] string? cursor, CancellationToken ct) => ExecuteWrite(async actor =>
    {
        var preferences = await ParticipantPreferences.Get(actor, ct) ?? new ParticipantPreferences { Id = actor };
        preferences.NotificationsReadAt = clock.GetUtcNow();
        await preferences.Save(ct);
        return Ok(new { readAt = preferences.NotificationsReadAt });
    });

    private async Task<IActionResult> ExecuteWrite(Func<string, Task<IActionResult>> action) { await gate.Enter(HttpContext.RequestAborted); try { return await Execute(action); } finally { gate.Exit(); } }

    private async Task<IActionResult> Execute(Func<string, Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { using var fresh = EntityContext.NoCache(); var actor = Actor(); await activity.EnsureParticipantActive(actor, User.FindFirst(ParticipationConstants.CredentialClaim)?.Value, HttpContext.RequestAborted); return await action(actor); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ArgumentException error) { return BadRequest(new { message = error.Message }); }
    }
}

public sealed record PreferencePatch(string? Theme = null, bool? NotifyReplies = null,
    bool? NotifyMentions = null, bool? NotifyFollowing = null, bool? NotifyNewPeople = null, string? RepliesPlacement = null);
