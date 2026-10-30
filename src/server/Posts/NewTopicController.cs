using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Render;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Where a Topic starts: <c>/t/{tangent}/new</c>, the Tangent page's "new topic". Its <c>GET</c> is the form,
/// one resource both ways (<see cref="NewTopicPage"/>), with a TID minted into it. Its <c>POST</c> takes <c>title</c>,
/// <c>body</c>, <c>kind</c> and <c>id</c>, as a form or a JSON object, and sends <see cref="StartTopic"/>: a form is
/// sent on to the new Post's address, a JSON request reads the Post (<c>201</c>); a refusal reads the form again with the
/// reason inline, or a problem with the gate; the same id with other words is a conflict. Both resolve the address as
/// the Topic page does (<see cref="PostWrites.Locate"/>) before anything else is judged, so a Tangent the reader may not
/// view and one no row has answer alike.</summary>
public sealed class NewTopicController(KoanWebBaseUrl baseUrl, WriteLoop writer) : ControllerBase
{
    public const string Route = "/t/{tangent}/" + TangentPageController.NewTopicSegment;

    [HttpGet(Route)]
    public async Task<IActionResult> Get(string tangent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var (found, unseen) = await PostWrites.Locate(personId, tangent, null, null, ct);
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
        var (found, unseen) = await PostWrites.Locate(personId, tangent, null, null, ct);
        if (found is null) return unseen!;
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();

        var kind = PostWrites.Kind(fields, out var kindValid);
        if (!kindValid) return await Refused(tangent, personId, fields, PostWrites.KindRule, ct);
        var outcome = await writer.Send(new StartTopic(
            fields.GetValueOrDefault(PostWrites.IdField) ?? "",
            found.Tangent.Id,
            fields.GetValueOrDefault(PostWrites.TitleField) ?? "",
            await PostWrites.Body(fields, found.Capabilities.Holds(Bit.Topic), ct),
            personId ?? "",
            kind), ct);
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var root }:
                return await PostWrites.Written(HttpContext, root, root, PostWrites.Address(BaseAddress(), root.TangentId, root.Slug ?? root.Id, root.Id),
                    personId, created: true, ct);
            case Outcome<Post>.Refused { Reason: StartTopic.NoTangent }:
                return PostWrites.Absent();
            case Outcome<Post>.Refused { Reason: var reason }:
                return await Refused(tangent, personId, fields, reason, ct);
            case Outcome<Post>.Conflict { Reason: var reason }:
                if (PostWrites.WantsJson(Request)) return PostWrites.Conflicting(reason);
                return await Again(tangent, personId, fields, StatusCodes.Status409Conflict, keepId: false, page => page with
                {
                    Failure = "This form was already sent with other words, so nothing was posted. Send it again to post these words as well."
                }, ct);
            case Outcome<Post>.Failed { Cause: var cause }:
                throw new InvalidOperationException($"Starting a topic in {tangent} failed.", cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for starting a topic in {tangent}.");
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again with the
    /// reason where it belongs, and a new Id where the sent one is not available.</summary>
    private async Task<IActionResult> Refused(string tangent, string? personId, IReadOnlyDictionary<string, string> fields, string reason,
        CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return PostWrites.Refused(reason);
        return await Again(tangent, personId, fields, PostWrites.Status(reason), keepId: reason != Post.IdTaken, page => PostWrites.Concerning(reason) switch
        {
            PostWrites.About.Title => page with { TitleFailure = reason },
            PostWrites.About.Body => page with { BodyFailure = reason },
            PostWrites.About.Sent => page with { Failure = reason },
            _ => page with { Gate = reason }
        }, ct);
    }

    /// <summary>The form again, read afresh, keeping the words that were sent, and the Id when it is a TID and
    /// <paramref name="keepId"/>; otherwise a new one is minted, so sending again starts a Topic of its own.</summary>
    private async Task<IActionResult> Again(string tangent, string? personId, IReadOnlyDictionary<string, string> fields, int status, bool keepId,
        Func<NewTopicPage, NewTopicPage> refusal, CancellationToken ct)
    {
        var (found, unseen) = await PostWrites.Locate(personId, tangent, null, null, ct);
        if (found is null) return unseen!;
        var page = await Page(found, personId, ct) with
        {
            Title = fields.GetValueOrDefault(PostWrites.TitleField),
            Body = fields.GetValueOrDefault(PostWrites.BodyField)
        };
        if (keepId && fields.GetValueOrDefault(PostWrites.IdField) is { } id && Tid.IsValid(id)) page = page with { Id = id };
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
            personId is null ? baseAddress + Participants.SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, capabilities, baseAddress, here, ct));
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
