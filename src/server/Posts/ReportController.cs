using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Render;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>What a report sends, as a form or one JSON object.</summary>
public sealed record ReportInput(
    [Description(Holds.Reason)] string? Body = null,
    [Description(Holds.Id)] string? Id = null);

/// <summary>Where a Post is reported: <c>/t/{tangent}/{topic}/{post}/report</c>, beside its edit. Its <c>POST</c> takes
/// <see cref="ReportInput"/> and sends <see cref="ReportPost"/>: a form is sent
/// on to this address with <c>received</c> naming the report, and a JSON request reads that the report was received;
/// neither is told where the report is. A refusal reads the form again with the reason inline, or a problem with its word.
/// The same id from the same reporter about the same Post reads that the report was received, whatever became of it. Its <c>GET</c> is the form, one resource both ways
/// (<see cref="ReportPage"/>), with a TID minted into it, or, where <c>received</c> names a report the reader made of this
/// Post, that it was received. Both need a signed-in participant who may view the Post, which the Doorway judges before
/// anything is bound (<see cref="Access.Doorway"/>), so a Post the reader may not view and one no row has answer alike.</summary>
public sealed class ReportController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The segment under a Post's address where it is reported.</summary>
    public const string Segment = "report";

    public const string Route = "/t/{tangent}/{topic}/{post}/" + Segment;

    /// <summary>The query parameter that names a report the reader made, to read that it was received.</summary>
    public const string ReceivedParameter = "received";

    [HttpGet(Route)]
    [Needs(Bit.View, Participant = true)]
    public async Task<IActionResult> Get([FromRoute] string tangent, [FromRoute] string topic, [FromRoute] string post,
        [FromQuery(Name = ReceivedParameter)] string? received, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        var personId = found.PersonId!;
        var theirs = Tid.IsValid(received) && found.Rows.Space.ModeratorsTangentId is { } moderators
                     && await Post.Get(received!, ct) is { } report && await ReportPost.IsReport(report, moderators, personId, found.Post!.Id, ct);
        var page = await Page(found, personId, theirs, ct);
        return page.Failure == ReportPost.InModerators.Text ? new ObjectResult(page) { StatusCode = StatusCodes.Status400BadRequest } : Ok(page);
    }

    [HttpPost(Route)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "report", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Needs(Bit.View, Participant = true)]
    [Description("Tell the moderators about someone else's Post, in a sentence or two, when it needs them rather than a reply; its author is never told. Send an id, a TID you mint, and the same call again returns the same report, so a retry is safe; without one, each call is another word to them.")]
    public async Task<IActionResult> Report([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        [FromRoute, Description(Holds.Post)] string post, ReportInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        var personId = found.PersonId!;

        var body = await PostWrites.Body(input.Body, !found.Post!.Removed);
        var outcome = await new ReportPost(PostWrites.Id(input.Id), found.Post!.Id, body, personId).Send();
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var report }:
                if (PostWrites.WantsJson(Request)) return Ok(await Page(found, personId, received: true, ct));
                Response.Headers.Location = Address(found) + "/" + Segment + "?" + ReceivedParameter + "=" + Uri.EscapeDataString(report.Id);
                return StatusCode(StatusCodes.Status303SeeOther);
            case Outcome<Post>.Refused { Reason: { Kind: not ReasonKind.Absent } reason }:
                return await Refused(found, personId, input, reason, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again with the
    /// reason where it belongs, and a new Id where the sent one is another's.</summary>
    private async Task<IActionResult> Refused(Here found, string? personId, ReportInput input, Reason reason, CancellationToken ct)
    {
        var unsent = reason == ReportPost.NoModerators;
        if (PostWrites.WantsJson(Request))
            return unsent ? new Problem(StatusCodes.Status503ServiceUnavailable, reason.Text) { Title = "Not sent" }.ToResult() : Answers.Refused(HttpContext, reason);

        return await Again(found, personId, input, unsent ? StatusCodes.Status503ServiceUnavailable : Answers.Status(HttpContext, reason), keepId: reason.Kind != ReasonKind.Conflict,
            page => reason == ReportPost.NeedsReason || PostWrites.Concerning(reason) == PostWrites.About.Body ? page with { BodyFailure = reason.Text }
                : reason.Kind == ReasonKind.Gated ? page with { Gate = reason.Word, Report = null }
                : page with { Failure = reason.Text }, ct);
    }

    /// <summary>The form again, read afresh, keeping the reason that was sent, and the Id when it is a TID and
    /// <paramref name="keepId"/>; otherwise a new one is minted, so sending again is a report of its own.</summary>
    private async Task<IActionResult> Again(Here sent, string? personId, ReportInput input, int status,
        bool keepId, Func<ReportPage, ReportPage> refusal, CancellationToken ct)
    {
        var (found, refused) = await Here.Find(personId, sent.Tangent!.Id, sent.Root!.Slug ?? sent.Root.Id, sent.Post!.Id, ct);
        if (refused is not null) return this.Refused(refused);
        var page = await Page(found, personId, received: false, ct) with { Body = input.Body };
        if (keepId && Tid.IsValid(input.Id)) page = page with { Report = page.Report?.With(PostWrites.IdField, input.Id) };
        return new ObjectResult(refusal(page)) { StatusCode = status };
    }

    /// <summary>The page for the Post the address names: the Post quoted, and the form — with a gate where the reader
    /// may not report it, <see cref="Capabilities.Removed"/> for a tombstone — or, where <paramref name="received"/>, only
    /// that the report was received. A Post of the moderators'
    /// Tangent carries no form and no Id, only <see cref="ReportPost.InModerators"/>, as sending one is answered.</summary>
    private async Task<ReportPage> Page(Here found, string? personId, bool received, CancellationToken ct)
    {
        var reported = found.Post!;
        var root = found.Root!;
        var baseAddress = BaseAddress();
        var tangent = found.Tangent!;
        var tangentAddress = PostWrites.TangentAddress(baseAddress, tangent.Id);
        var topicAddress = PostWrites.TopicAddress(baseAddress, tangent.Id, root.Slug ?? root.Id);
        var address = Address(found);
        var back = baseUrl.PathBase + "/t/" + tangent.Id + "/" + (root.Slug ?? root.Id) + "/" + reported.Id + "/" + Segment;
        var gate = received ? null : reported.Removed ? Capabilities.Removed.Word : null;
        var inModerators = !received && reported.TangentId == found.Rows.Space.ModeratorsTangentId;

        var people = await TakeOnATangentController.People(reported, ct);
        var author = people.TryGetValue(reported.AuthorId, out var person)
            ? person
            : new TangentPage.Author(reported.AuthorId, reported.AuthorId, reported.AuthorId, "person", null);
        return new ReportPage(
            new TakeOnATangentPage.Quoted(reported.Id, address, author, reported.CreatedAt,
                reported.Removed ? null : TangentPage.Opening(reported, people), reported.Removed, root.Title ?? "", topicAddress,
                tangent.Name, tangentAddress, TangentPage.EditedOf(reported, people)),
            received,
            received || inModerators || gate is not null ? null : PostWrites.ReportForm(address + "/" + Segment, Tid.Mint(DateTimeOffset.UtcNow)),
            gate,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(back) : null,
            found.Rows.Space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, found.Rows.For(null), baseAddress, back, ct),
            Failure: inModerators ? ReportPost.InModerators.Text : null);
    }

    /// <summary>The reported Post's address.</summary>
    private string Address(Here found)
        => PostWrites.Address(BaseAddress(), found.Tangent!.Id, found.Root!.Slug ?? found.Root.Id, found.Post!.Id);

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
