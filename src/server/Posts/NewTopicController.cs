using Koan.Data.Core.Commands;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>Where a Topic starts: <c>/t/{tangent}/new</c>, the Tangent page's "new topic". Its <c>GET</c> is the form,
/// one resource both ways (<see cref="NewTopicPage"/>), with a TID minted into it. Its <c>POST</c> takes <c>title</c>,
/// <c>body</c>, <c>kind</c> and <c>id</c>, as a form or a JSON object, and sends <see cref="StartTopic"/>: a form is
/// sent on to the new Post's address, a JSON request reads the Post (<c>201</c>); a refusal reads the form again with the
/// reason inline, or a problem with the gate; the same id with other words is a conflict. Both resolve the address as
/// the Topic page does (<see cref="PostWrites.Locate"/>) before anything else is judged, so a Tangent the reader may not
/// view and one no row has answer alike.</summary>
public sealed class NewTopicController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Route = "/t/{tangent}/" + TangentPageController.NewTopicSegment;

    [HttpGet(Route)]
    public async Task<IActionResult> Get(string tangent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, tangent, null, null, ct);
        if (found is null) return unseen!;
        return Ok(await Page(found, personId, ct));
    }

    [HttpPost(Route)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public async Task<IActionResult> Start(string tangent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, tangent, null, null, ct);
        if (found is null) return unseen!;
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();

        var kind = PostWrites.Kind(fields, out var kindValid);
        if (!kindValid) return await Refused(tangent, personId, fields, PostWrites.KindRule, ct);
        var outcome = await new StartTopic(
            fields.GetValueOrDefault(PostWrites.IdField) ?? "",
            found.Tangent.Id,
            PostWrites.Title(fields) ?? "",
            await PostWrites.Body(fields, found.Capabilities.Holds(Bit.Topic)),
            personId ?? "",
            kind,
            CoverId: fields.GetValueOrDefault(PostWrites.CoverField) is { Length: > 0 } cover ? cover : null).Send();
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var root }:
                return await PostWrites.Written(HttpContext, root, root, PostWrites.Address(BaseAddress(), root.TangentId, root.Slug ?? root.Id, root.Id),
                    personId, created: true, ct);
            case Outcome<Post>.Refused { Reason: { Kind: not (ReasonKind.Absent or ReasonKind.Conflict) } reason }:
                return await Refused(tangent, personId, fields, reason, ct);
            case Outcome<Post>.Refused { Reason: { Kind: ReasonKind.Conflict } reason }:
                if (PostWrites.WantsJson(Request)) return PostWrites.Refused(HttpContext, reason);
                return await Again(tangent, personId, fields, StatusCodes.Status409Conflict, keepId: false, page => page with
                {
                    Failure = "This form was already sent with other words, so nothing was posted. Send it again to post these words as well."
                }, ct);
            default:
                return PostWrites.Answer(HttpContext, outcome);
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again with the
    /// reason where it belongs.</summary>
    private async Task<IActionResult> Refused(string tangent, string? personId, IReadOnlyDictionary<string, string> fields, Reason reason,
        CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return PostWrites.Refused(HttpContext, reason);
        return await Again(tangent, personId, fields, PostWrites.Status(reason), keepId: true, page => PostWrites.Concerning(reason) switch
        {
            PostWrites.About.Title => page with { TitleFailure = reason.Text },
            PostWrites.About.Body => page with { BodyFailure = reason.Text },
            PostWrites.About.Sent => page with { Failure = reason.Text },
            _ => page with { Gate = reason.Word, Start = null }
        }, ct);
    }

    /// <summary>The form again, read afresh, keeping the words that were sent, and the Id when it is a TID and
    /// <paramref name="keepId"/>; otherwise a new one is minted, so sending again starts a Topic of its own.</summary>
    private async Task<IActionResult> Again(string tangent, string? personId, IReadOnlyDictionary<string, string> fields, int status, bool keepId,
        Func<NewTopicPage, NewTopicPage> refusal, CancellationToken ct)
    {
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, tangent, null, null, ct);
        if (found is null) return unseen!;
        var page = await Page(found, personId, ct) with
        {
            Title = PostWrites.Title(fields),
            Body = fields.GetValueOrDefault(PostWrites.BodyField),
            CoverId = fields.GetValueOrDefault(PostWrites.CoverField) is { Length: > 0 } cover ? cover : null
        };
        if (keepId && fields.GetValueOrDefault(PostWrites.IdField) is { } id && Tid.IsValid(id)) page = page with { Start = page.Start?.With(PostWrites.IdField, id) };
        return new ObjectResult(refusal(page)) { StatusCode = status };
    }

    private async Task<NewTopicPage> Page(PostWrites.Located found, string? personId, CancellationToken ct)
    {
        var row = found.Tangent;
        var capabilities = found.Capabilities;
        var baseAddress = BaseAddress();
        var address = PostWrites.TangentAddress(baseAddress, row.Id);
        var here = baseUrl.PathBase + "/t/" + row.Id + "/" + TangentPageController.NewTopicSegment;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        var gate = capabilities.Gate(Bit.Topic)?.Word;
        return new NewTopicPage(
            row.Id,
            row.Name,
            address,
            PostWrites.Word(row.DefaultKind),
            gate is null ? PostWrites.StartForm(address + "/" + TangentPageController.NewTopicSegment, row.DefaultKind, FormToken.For(HttpContext)) : null,
            capabilities.Can,
            gate,
            personId is null ? baseAddress + Participants.SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, capabilities, baseAddress, here, ct),
            Bring: gate is null && row.DefaultKind == PostKind.Article ? PostWrites.MediaForm(address, FormToken.For(HttpContext)) : null,
            Day: DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
