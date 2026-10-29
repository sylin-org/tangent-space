using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Where a Topic starts: <c>/t/{tangent}/new</c>, the Tangent page's "new topic". Its <c>GET</c> is the form,
/// one resource both ways (<see cref="NewTopicPage"/>), with a TID minted into it; a reader without <see cref="Bit.View"/>
/// on the Tangent receives its gate, and one for whom it is absent what an address with no Tangent receives. Its
/// <c>POST</c> takes <c>title</c>, <c>body</c>, <c>kind</c> and <c>id</c>, as a form or a JSON object, and sends
/// <see cref="StartTopic"/>: a form is sent on to the new Post's address, a JSON request reads the Post (<c>201</c>);
/// a refusal reads the form again with the reason inline, or a problem with the gate; the same id with other words is a
/// conflict.</summary>
public sealed class NewTopicController(KoanWebBaseUrl baseUrl, WriteLoop writer) : ControllerBase
{
    public const string Route = "/t/{tangent}/" + TangentPageController.NewTopicSegment;

    [HttpGet(Route)]
    public async Task<IActionResult> Get(string tangent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var capabilities = await Capabilities.Load(personId, tangent, null, ct);
        if (!capabilities.Exists) return PostWrites.Unseen(capabilities.Gate(Bit.View));
        if (await Tangents.Tangent.Get(tangent, ct) is not { } row) return PostWrites.Absent();
        return Ok(await Page(row, capabilities, personId, ct));
    }

    [HttpPost(Route)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public async Task<IActionResult> Start(string tangent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();

        var kind = PostWrites.Kind(fields, out var kindValid);
        if (!kindValid) return await Refused(tangent, personId, fields, PostWrites.KindRule, ct);
        var outcome = await writer.Send(new StartTopic(
            fields.GetValueOrDefault(PostWrites.IdField) ?? "",
            tangent,
            fields.GetValueOrDefault(PostWrites.TitleField) ?? "",
            fields.GetValueOrDefault(PostWrites.BodyField) ?? "",
            personId ?? "",
            kind), ct);
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var root }:
                return await PostWrites.Written(HttpContext, root, PostWrites.Address(Configured(), root.TangentId, root.Slug ?? root.Id, root.Id),
                    personId, created: true, ct);
            case Outcome<Post>.Refused { Reason: StartTopic.NoTangent }:
                return PostWrites.Absent();
            case Outcome<Post>.Refused { Reason: var reason }:
                return await Refused(tangent, personId, fields, reason, ct);
            case Outcome<Post>.Conflict { Reason: var reason }:
                if (PostWrites.WantsJson(Request)) return PostWrites.Conflicting(reason);
                return await Again(tangent, personId, fields, StatusCodes.Status409Conflict, page => page with
                {
                    // A new Id, so sending these words again starts a Topic of their own.
                    Failure = "This form was already sent with other words, so nothing was posted. Send it again to post these words as well."
                }, ct);
            case Outcome<Post>.Failed { Cause: var cause }:
                throw new InvalidOperationException($"Starting a topic in {tangent} failed.", cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for starting a topic in {tangent}.");
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again with the
    /// reason where it belongs.</summary>
    private async Task<IActionResult> Refused(string tangent, string? personId, IReadOnlyDictionary<string, string> fields, string reason,
        CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return PostWrites.Refused(reason);
        return await Again(tangent, personId, fields, PostWrites.Status(reason), page => PostWrites.Concerning(reason) switch
        {
            PostWrites.About.Title => page with { TitleFailure = reason },
            PostWrites.About.Body => page with { BodyFailure = reason },
            PostWrites.About.Sent => page with { Failure = reason },
            _ => page with { Gate = reason }
        }, ct);
    }

    /// <summary>The form again, keeping the words that were sent and the Id when it is a TID, at this status.</summary>
    private async Task<IActionResult> Again(string tangent, string? personId, IReadOnlyDictionary<string, string> fields, int status,
        Func<NewTopicPage, NewTopicPage> refusal, CancellationToken ct)
    {
        var capabilities = await Capabilities.Load(personId, tangent, null, ct);
        if (!capabilities.Exists || await Tangents.Tangent.Get(tangent, ct) is not { } row) return PostWrites.Absent();
        var page = await Page(row, capabilities, personId, ct) with
        {
            Title = fields.GetValueOrDefault(PostWrites.TitleField),
            Body = fields.GetValueOrDefault(PostWrites.BodyField)
        };
        if (status != StatusCodes.Status409Conflict && fields.GetValueOrDefault(PostWrites.IdField) is { } id && Tid.IsValid(id))
            page = page with { Id = id };
        return new ObjectResult(refusal(page)) { StatusCode = status };
    }

    private async Task<NewTopicPage> Page(Tangents.Tangent row, Capabilities capabilities, string? personId, CancellationToken ct)
    {
        var configured = Configured();
        var address = PostWrites.TangentAddress(configured, row.Id);
        var here = baseUrl.PathBase + "/t/" + row.Id + "/" + TangentPageController.NewTopicSegment;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        return new NewTopicPage(
            row.Id,
            row.Name,
            address,
            PostWrites.Word(row.DefaultKind),
            address + "/" + TangentPageController.NewTopicSegment,
            FormToken.For(HttpContext),
            Tid.Mint(DateTimeOffset.UtcNow),
            capabilities.Can,
            capabilities.Gate(Bit.Topic),
            personId is null ? configured + Participants.SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            configured + "/",
            await PostWrites.Reader(personId, capabilities, configured, here, ct));
    }

    private string Configured() => baseUrl.Configured
        ?? throw new InvalidOperationException("Koan:Web:ApplicationUrl must be set to the base URL the Tangent Space answers at.");
}
