using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Render;
using Tangent.Tangents;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Where a Post is reported: <c>/t/{tangent}/{topic}/{post}/report</c>, beside its edit. Its <c>POST</c> takes
/// <c>body</c>, the reason, and <c>id</c>, as a form or a JSON object, and sends <see cref="ReportPost"/>: a form is sent
/// on to this address with <c>received</c> naming the report, and a JSON request reads that the report was received;
/// neither is told where the report is. A refusal reads the form again with the reason inline, or a problem with the gate;
/// the same id with another reason is a conflict. Its <c>GET</c> is the form, one resource both ways
/// (<see cref="ReportPage"/>), with a TID minted into it, or, where <c>received</c> names a report the reader made of this
/// Post, that it was received. Both resolve the address as the Topic page does (<see cref="PostWrites.Locate"/>) before
/// anything else is judged, so a Post the reader may not view and one no row has answer alike.</summary>
public sealed class ReportController(KoanWebBaseUrl baseUrl, WriteLoop writer) : ControllerBase
{
    /// <summary>The segment under a Post's address where it is reported.</summary>
    public const string Segment = "report";

    public const string Route = "/t/{tangent}/{topic}/{post}/" + Segment;

    /// <summary>The query parameter that names a report the reader made, to read that it was received.</summary>
    public const string ReceivedParameter = "received";

    [HttpGet(Route)]
    public async Task<IActionResult> Get([FromRoute] string tangent, [FromRoute] string topic, [FromRoute] string post,
        [FromQuery(Name = ReceivedParameter)] string? received, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var (found, unseen) = await PostWrites.Locate(personId, tangent, topic, post, ct);
        if (found is null) return unseen!;
        var theirs = personId is not null && Tid.IsValid(received) && found.Rows.Space.ModeratorsTangentId is { } moderators
                     && await Post.Get(received!, ct) is { } report && await ReportPost.IsReport(report, moderators, personId, found.Post!.Id, ct);
        return Ok(await Page(found, personId, theirs, ct));
    }

    [HttpPost(Route)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public async Task<IActionResult> Report([FromRoute] string tangent, [FromRoute] string topic, [FromRoute] string post, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        var (found, unseen) = await PostWrites.Locate(personId, tangent, topic, post, ct);
        if (found is null) return unseen!;
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();

        var mayReport = found.Rows.Participant is not null && !found.Post!.Removed;
        var outcome = await writer.Send(new ReportPost(
            fields.GetValueOrDefault(PostWrites.IdField) ?? "",
            found.Post!.Id,
            PostWrites.Body(fields, mayReport),
            personId ?? ""), ct);
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var report }:
                if (PostWrites.WantsJson(Request)) return Ok(await Page(found, personId, received: true, ct));
                Response.Headers.Location = Address(found) + "/" + Segment + "?" + ReceivedParameter + "=" + Uri.EscapeDataString(report.Id);
                return StatusCode(StatusCodes.Status303SeeOther);
            case Outcome<Post>.Refused { Reason: ReportPost.NoPost }:
                return PostWrites.Absent();
            case Outcome<Post>.Refused { Reason: var reason }:
                return await Refused(found, personId, fields, reason, ct);
            case Outcome<Post>.Conflict { Reason: var reason }:
                if (PostWrites.WantsJson(Request)) return PostWrites.Conflicting(reason);
                return await Again(found, personId, fields, StatusCodes.Status409Conflict, keepId: false, page => page with
                {
                    Failure = "This form was already sent with another reason, so nothing was sent. Send it again to send this reason as well."
                }, ct);
            case Outcome<Post>.Failed { Cause: var cause }:
                throw new InvalidOperationException($"Reporting {post} failed.", cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for reporting {post}.");
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again with the
    /// reason where it belongs, and a new Id where the sent one is not available.</summary>
    private async Task<IActionResult> Refused(PostWrites.Located found, string? personId, IReadOnlyDictionary<string, string> fields, string reason,
        CancellationToken ct)
    {
        var about = reason switch
        {
            ReportPost.NeedsReason => PostWrites.About.Body,
            ReportPost.NoModerators => PostWrites.About.Sent,
            _ => PostWrites.Concerning(reason)
        };
        if (PostWrites.WantsJson(Request))
        {
            return reason switch
            {
                ReportPost.NoModerators => new Problem(StatusCodes.Status503ServiceUnavailable, "Not sent", reason).ToResult(),
                ReportPost.NeedsReason => new Problem(StatusCodes.Status400BadRequest, "Not sent", reason).ToResult(),
                _ => PostWrites.Refused(reason)
            };
        }

        var status = reason switch
        {
            ReportPost.NoModerators => StatusCodes.Status503ServiceUnavailable,
            ReportPost.NeedsReason => StatusCodes.Status400BadRequest,
            _ => PostWrites.Status(reason)
        };
        return await Again(found, personId, fields, status, keepId: reason != Post.IdTaken, page => about switch
        {
            PostWrites.About.Body => page with { BodyFailure = reason },
            PostWrites.About.Gate => page with { Gate = reason },
            _ => page with { Failure = reason }
        }, ct);
    }

    /// <summary>The form again, read afresh, keeping the reason that was sent, and the Id when it is a TID and
    /// <paramref name="keepId"/>; otherwise a new one is minted, so sending again is a report of its own.</summary>
    private async Task<IActionResult> Again(PostWrites.Located sent, string? personId, IReadOnlyDictionary<string, string> fields, int status,
        bool keepId, Func<ReportPage, ReportPage> refusal, CancellationToken ct)
    {
        var (found, unseen) = await PostWrites.Locate(personId, sent.Tangent.Id, sent.Root!.Slug ?? sent.Root.Id, sent.Post!.Id, ct);
        if (found is null) return unseen!;
        var page = await Page(found, personId, received: false, ct) with { Body = fields.GetValueOrDefault(PostWrites.BodyField) };
        if (keepId && fields.GetValueOrDefault(PostWrites.IdField) is { } id && Tid.IsValid(id)) page = page with { Id = id };
        return new ObjectResult(refusal(page)) { StatusCode = status };
    }

    /// <summary>The page for the Post the address names: the Post quoted, and the form — with a gate where the reader
    /// may not report it: <see cref="Capabilities.SignIn"/> for a visitor, <see cref="Capabilities.Removed"/> for a
    /// tombstone — or, where <paramref name="received"/>, only that the report was received.</summary>
    private async Task<ReportPage> Page(PostWrites.Located found, string? personId, bool received, CancellationToken ct)
    {
        var reported = found.Post!;
        var root = found.Root!;
        var configured = Configured();
        var tangentAddress = PostWrites.TangentAddress(configured, found.Tangent.Id);
        var topicAddress = PostWrites.TopicAddress(configured, found.Tangent.Id, root.Slug ?? root.Id);
        var address = Address(found);
        var back = baseUrl.PathBase + "/t/" + found.Tangent.Id + "/" + (root.Slug ?? root.Id) + "/" + reported.Id + "/" + Segment;
        var reader = found.Rows.Participant;
        var gate = received ? null : reader is null ? Capabilities.SignIn : reported.Removed ? Capabilities.Removed : null;

        var people = await TakeOnATangentController.People(reported, ct);
        var author = people.TryGetValue(reported.AuthorId, out var person)
            ? person
            : new TangentPage.Author(reported.AuthorId, reported.AuthorId, reported.AuthorId, "person", null);
        return new ReportPage(
            new TakeOnATangentPage.Quoted(reported.Id, address, author, reported.CreatedAt,
                reported.Removed ? null : TangentPage.Opening(reported, people), reported.Removed, root.Title ?? "", topicAddress,
                found.Tangent.Name, tangentAddress),
            received,
            address + "/" + Segment,
            received || reader is null ? null : FormToken.For(HttpContext),
            received || gate is not null ? null : Tid.Mint(DateTimeOffset.UtcNow),
            gate,
            personId is null ? configured + SignInController.Path + "?return=" + Uri.EscapeDataString(back) : null,
            found.Rows.Space.Name,
            configured + "/",
            await PostWrites.Reader(personId, found.Rows.For(null), configured, back, ct));
    }

    /// <summary>The reported Post's address.</summary>
    private string Address(PostWrites.Located found)
        => PostWrites.Address(Configured(), found.Tangent.Id, found.Root!.Slug ?? found.Root.Id, found.Post!.Id);

    private string Configured() => baseUrl.Configured
        ?? throw new InvalidOperationException("Koan:Web:ApplicationUrl must be set to the base URL the Tangent Space answers at.");
}
