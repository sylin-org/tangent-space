using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>What starting a Topic sends, as a form or one JSON object.</summary>
public sealed record StartInput(
    [Description(Holds.TopicTitle)] string? Title = null,
    [Description(Holds.TopicBody)] string? Body = null,
    [Description(Holds.Cover)] string? Cover = null,
    [Description(Holds.Id)] string? Id = null);

/// <summary>Where a Topic starts: <c>/t/{tangent}/new</c>, the Tangent page's "new topic". Its <c>GET</c> is the form,
/// one resource both ways (<see cref="NewTopicPage"/>), with a TID minted into it. Its <c>POST</c> takes
/// <see cref="StartInput"/> and sends <see cref="StartTopic"/>: a form is sent on to the new Post's address, a JSON
/// request reads the Post (<c>201</c>); a refusal reads the form again with the reason inline, or a problem with its word;
/// the same id with other words is a conflict. Both need <see cref="Bit.Topic"/>, which the Doorway judges before anything is
/// bound (<see cref="Access.Doorway"/>), so a Tangent the reader may not view and one no row has answer alike.</summary>
public sealed class NewTopicController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Route = "/t/{tangent}/" + TangentPageController.NewTopicSegment;

    [HttpGet(Route)]
    [Needs(Bit.Topic)]
    public async Task<IActionResult> Get(string tangent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        return Ok(await Page(found, found.PersonId, ct));
    }

    [HttpPost(Route)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "start_topic", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Needs(Bit.Topic)]
    [Description("Start a Topic in a Tangent, when what you have to say answers nothing already there. Send an id, a TID you mint, and the same call again returns the same Topic, so a retry is safe; without one, each call makes a new Topic. Refused, its word says why.")]
    public async Task<IActionResult> Start([FromRoute, Description(Holds.Tangent)] string tangent, StartInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        var personId = found.PersonId;

        var title = PostWrites.Title(input.Title) ?? "";
        var cover = input.Cover is { Length: > 0 } named ? named : null;
        var body = await PostWrites.Body(input.Body, found.Capabilities.Holds(Bit.Topic));
        var outcome = await new StartTopic(
            PostWrites.Id(input.Id),
            found.Tangent!.Id,
            title,
            body,
            personId ?? "",
            CoverId: cover).Send();
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var root }:
                return await PostWrites.Written(HttpContext, root, root, PostWrites.Address(BaseAddress(), root.TangentId, root.Slug ?? root.Id, root.Id),
                    personId, created: true, ct);
            case Outcome<Post>.Refused { Reason: { Kind: not (ReasonKind.Absent or ReasonKind.Conflict) } reason }:
                return await Refused(tangent, personId, input, reason, ct);
            case Outcome<Post>.Refused { Reason: { Kind: ReasonKind.Conflict } reason }:
                if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);
                return await Again(tangent, personId, input, StatusCodes.Status409Conflict, keepId: false, page => page with
                {
                    Failure = "This form was already sent with other words, so nothing was posted. Send it again to post these words as well."
                }, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again with the
    /// reason where it belongs.</summary>
    private async Task<IActionResult> Refused(string tangent, string? personId, StartInput input, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);
        return await Again(tangent, personId, input, Answers.Status(HttpContext, reason), keepId: true, page => PostWrites.Concerning(reason) switch
        {
            PostWrites.About.Title => page with { TitleFailure = reason.Text },
            PostWrites.About.Body => page with { BodyFailure = reason.Text },
            PostWrites.About.Sent => page with { Failure = reason.Text },
            _ => page with { Gate = reason.Word, Start = null }
        }, ct);
    }

    /// <summary>The form again, read afresh, keeping the words that were sent, and the Id when it is a TID and
    /// <paramref name="keepId"/>; otherwise a new one is minted, so sending again starts a Topic of its own.</summary>
    private async Task<IActionResult> Again(string tangent, string? personId, StartInput input, int status, bool keepId,
        Func<NewTopicPage, NewTopicPage> refusal, CancellationToken ct)
    {
        var (found, refused) = await Here.Find(personId, tangent, null, null, ct);
        if (refused is not null) return this.Refused(refused);
        var page = await Page(found, personId, ct) with
        {
            Title = PostWrites.Title(input.Title),
            Body = input.Body,
            CoverId = input.Cover is { Length: > 0 } cover ? cover : null
        };
        if (keepId && Tid.IsValid(input.Id)) page = page with { Start = page.Start?.With(PostWrites.IdField, input.Id) };
        return new ObjectResult(refusal(page)) { StatusCode = status };
    }

    private async Task<NewTopicPage> Page(Here found, string? personId, CancellationToken ct)
    {
        var row = found.Tangent!;
        var capabilities = found.Capabilities;
        var baseAddress = BaseAddress();
        var address = PostWrites.TangentAddress(baseAddress, row.Id);
        var here = baseUrl.PathBase + "/t/" + row.Id + "/" + TangentPageController.NewTopicSegment;
        var space = found.Rows.Space;
        var gate = capabilities.Gate(Bit.Topic)?.Word;
        return new NewTopicPage(
            row.Id,
            row.Name,
            address,
            PostWrites.Word(row.DefaultKind),
            gate is null ? PostWrites.StartForm(address + "/" + TangentPageController.NewTopicSegment, row.DefaultKind) : null,
            capabilities.Can,
            gate,
            personId is null ? baseAddress + Participants.SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, capabilities, baseAddress, here, ct),
            Bring: gate is null && row.DefaultKind == PostKind.Article ? PostWrites.MediaForm(address) : null,
            Day: DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
