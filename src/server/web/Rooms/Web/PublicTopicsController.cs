using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TangentSpace.Communities;
using TangentSpace.Conversation;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;

namespace TangentSpace.Rooms.Web;

/// <summary>An unlisted direct-read mount. Koan constrains the row; this controller only narrows its route and projection.</summary>
[ApiController, AllowAnonymous, Route("api/v1/public/tangents/{tangentId}/topics")]
public sealed class PublicTopicsController(ParticipantDirectory directory, PolicyGate policyGate) : EntityController<Room, string>
{
    private const int MaximumPosts = 25;
    private const int ResultBudget = 64 * 1024;
    private const int EnvelopeReserve = 1024;

    protected override QueryOptions BuildOptions()
    {
        var options = base.BuildOptions();
        var tangentKey = RouteData.Values["tangentId"]?.ToString() ?? "";
        options.AddPredicate<Room>(room => room.TangentKey == tangentKey);
        return options;
    }

    protected override ObjectResult PrepareResponse(object? content)
        => base.PrepareResponse(content is Room room ? PublicTopicDescription.From(room) : content);

    [HttpGet("{id}")]
    public override async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var response = await base.GetById(id, ct);
        if (response is NotFoundResult || response is ObjectResult { StatusCode: StatusCodes.Status404NotFound })
            return HiddenNotFound();
        if (response is not ObjectResult { Value: PublicTopicDescription topic }) return response;
        using var fresh = EntityContext.NoCache();
        return await TangentCommunity.Get(topic.TangentKey, ct) is null ? HiddenNotFound() : response;
    }

    [HttpGet("{id}/posts")]
    public async Task<IActionResult> GetPosts(string id, [FromQuery] long? before = null, [FromQuery] long? after = null,
        [FromQuery] int limit = MaximumPosts, CancellationToken ct = default)
    {
        await policyGate.Enter(ct);
        try
        {
            // Resolve through Koan first: failed credentials must win before caller query validation or data work.
            var topicResponse = await GetById(id, ct);
            if (topicResponse is not ObjectResult { Value: PublicTopicDescription topic }) return topicResponse;
            if (before is not null && after is not null) return BadRequest(new { reason = "Choose before or after, not both." });
            if (before is <= 0 || after is < 0 || limit is < 1 or > MaximumPosts)
                return BadRequest(new { reason = "Use before > 0, after >= 0, and a limit from 1 to 25." });

            var descending = before is not null || after is null;
            var query = MessageQuery(limit + 1, descending);
            IReadOnlyList<Message> candidates;
            using (EntityContext.NoCache())
            {
                candidates = before is { } older
                    ? await Message.Query(message => message.RoomKey == id && message.OfMessageId == null && message.Sequence < older, query, ct)
                    : after is { } newer
                        ? await Message.Query(message => message.RoomKey == id && message.OfMessageId == null && message.Sequence > newer, query, ct)
                        : await Message.Query(message => message.RoomKey == id && message.OfMessageId == null, query, ct);
            }

            var traversal = candidates.Take(limit).ToArray();
            var authors = traversal.Select(message => message.AuthorParticipantId).Distinct(StringComparer.Ordinal).ToArray();
            var labels = await directory.LabelsFor(authors, ct, resolveMissing: false);
            var refs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var author in authors) refs[author] = await directory.PerennialValue(author, ct);

            var selected = new List<(long Sequence, PublicPostDescription Post)>(traversal.Length);
            var bytes = EnvelopeReserve + JsonSerializer.SerializeToUtf8Bytes(topic).Length;
            foreach (var message in traversal)
            {
                var authorRef = refs[message.AuthorParticipantId];
                var label = labels.GetValueOrDefault(message.AuthorParticipantId, authorRef);
                var post = new PublicPostDescription(message.Id, new(authorRef, label),
                    message.Removed ? null : message.Content.Text, message.Content.CreatedAt, message.AcceptedAt,
                    message.EditedAt, message.Removed,
                    $"/t/{Uri.EscapeDataString(topic.TangentKey)}/{Uri.EscapeDataString(message.Id)}");
                var size = JsonSerializer.SerializeToUtf8Bytes(post).Length;
                if (bytes + size > ResultBudget) break;
                bytes += size;
                selected.Add((message.Sequence, post));
            }
            selected.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));

            long? olderBefore = null;
            long? newerAfter = null;
            if (selected.Count > 0)
            {
                var first = selected[0].Sequence;
                var last = selected[^1].Sequence;
                using var fresh = EntityContext.NoCache();
                if ((await Message.Query(message => message.RoomKey == id && message.OfMessageId == null && message.Sequence < first,
                        MessageQuery(1, descending: true), ct)).Count > 0)
                    olderBefore = first;
                if ((await Message.Query(message => message.RoomKey == id && message.OfMessageId == null && message.Sequence > last,
                        MessageQuery(1, descending: false), ct)).Count > 0)
                    newerAfter = last;
            }
            return Ok(new PublicPostWindow(topic, selected.Select(item => item.Post).ToArray(), olderBefore, newerAfter));
        }
        finally { policyGate.Exit(); }
    }

    [HttpGet("")]
    public override Task<IActionResult> GetCollection(CancellationToken ct) => Task.FromResult(HiddenNotFound());

    [HttpPost("query")]
    public override Task<IActionResult> Query(object body, CancellationToken ct) => Task.FromResult(HiddenNotFound());

    [HttpGet("new")]
    public override Task<IActionResult> GetNew(CancellationToken ct) => Task.FromResult(HiddenNotFound());

    [HttpPost("")]
    public override Task<IActionResult> Upsert(Room model, CancellationToken ct) => ReadOnly();

    [HttpPut("{id}")]
    public override Task<IActionResult> Put(string id, CancellationToken ct) => ReadOnly();

    [HttpPost("bulk")]
    public override Task<IActionResult> UpsertMany(IEnumerable<Room> models, CancellationToken ct) => ReadOnly();

    [HttpDelete("{id}")]
    public override Task<IActionResult> Delete(string id, CancellationToken ct) => ReadOnly();

    [HttpDelete("bulk")]
    public override Task<IActionResult> DeleteMany(IEnumerable<string> ids, CancellationToken ct) => ReadOnly();

    [HttpDelete("")]
    public override Task<IActionResult> DeleteByQuery(string? q, CancellationToken ct) => ReadOnly();

    [HttpDelete("all")]
    public override Task<IActionResult> DeleteAll(CancellationToken ct) => ReadOnly();

    [HttpPatch("{id}")]
    public override Task<IActionResult> Patch(string id, CancellationToken ct) => ReadOnly();

    private Task<IActionResult> ReadOnly()
        => Task.FromResult<IActionResult>(StatusCode(StatusCodes.Status405MethodNotAllowed));

    private static QueryDefinition MessageQuery(int size, bool descending)
    {
        var sequence = typeof(Message).GetProperty(nameof(Message.Sequence))!;
        return new QueryDefinition { Page = 1, PageSize = size,
            Sort = [new SortSpec(new MemberPath(typeof(Message), [sequence], typeof(long), false, -1), descending)] };
    }

    private IActionResult HiddenNotFound()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return new EmptyResult();
    }
}
