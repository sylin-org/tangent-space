using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>The place's settings: <c>/settings</c>, whose first section, general, names and describes the place. Its
/// <c>GET</c> is the form, one resource both ways (<see cref="TangentSpaceSettingsPage"/>). Its <c>POST</c> takes
/// <c>name</c> and <c>description</c>, as a form or a JSON object, each left as it is when not sent, and sends
/// <see cref="ChangeTangentSpace"/>: a form is sent on to the door, where the name and the description show; a JSON
/// request reads the place (<c>200</c>); a refusal reads the form again with the reason inline, or a problem with the
/// gate. A reader for whom the place does not exist receives what an address with nothing there receives.</summary>
public sealed class TangentSpaceSettingsController(KoanWebBaseUrl baseUrl, WriteLoop writer) : ControllerBase
{
    public const string Path = "/settings";

    public const string NameField = "name";
    public const string DescriptionField = "description";

    /// <summary>The section this page shows: what the place is called and how it describes itself.</summary>
    public const string General = "General";

    [HttpGet(Path)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        return Ok(await Page(place, personId, ct));
    }

    [HttpPost(Path)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public async Task<IActionResult> Change(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();

        var outcome = await writer.Send(new ChangeTangentSpace(personId ?? "", fields.GetValueOrDefault(NameField),
            fields.GetValueOrDefault(DescriptionField)), ct);
        switch (outcome)
        {
            case Outcome<TangentSpace>.Done { Result: var space }:
                return await Written(space, personId, ct);
            case Outcome<TangentSpace>.Refused { Reason: ChangeTangentSpace.NoPlace }:
                return PostWrites.Absent();
            case Outcome<TangentSpace>.Refused { Reason: var reason }:
                return await Refused(personId, fields, reason, ct);
            case Outcome<TangentSpace>.Failed { Cause: var cause }:
                throw new InvalidOperationException("Changing the Tangent Space's settings failed.", cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for changing the Tangent Space's settings.");
        }
    }

    /// <summary>A change that committed: a form is sent on to the door; a JSON request reads the place as it now stands,
    /// with the writer's <c>can</c> in it.</summary>
    private async Task<IActionResult> Written(TangentSpace space, string? personId, CancellationToken ct)
    {
        var door = BaseAddress() + "/";
        if (!PostWrites.WantsJson(Request))
        {
            Response.Headers.Location = door;
            return StatusCode(StatusCodes.Status303SeeOther);
        }

        var place = await Capabilities.Load(personId, null, null, ct);
        return new ObjectResult(new WrittenTangentSpace(space.Name, space.Description, door, BaseAddress() + Path, place.Can))
        {
            ContentTypes = { "application/json" }
        };
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again, read afresh,
    /// keeping what was sent, with the reason where it belongs.</summary>
    private async Task<IActionResult> Refused(string? personId, IReadOnlyDictionary<string, string> fields, string reason, CancellationToken ct)
    {
        var gated = reason is not (TangentSpace.NameRule or TangentSpace.DescriptionRule);
        if (PostWrites.WantsJson(Request))
            return gated ? PostWrites.Gated(reason) : new Problem(StatusCodes.Status400BadRequest, "Not written", reason).ToResult();

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        var page = await Page(place, personId, ct);
        page = page with
        {
            Name = fields.GetValueOrDefault(NameField) ?? page.Name,
            Description = fields.GetValueOrDefault(DescriptionField) ?? page.Description
        };
        page = reason switch
        {
            TangentSpace.NameRule => page with { NameFailure = reason },
            TangentSpace.DescriptionRule => page with { DescriptionFailure = reason },
            _ => page with { Gate = reason }
        };
        return new ObjectResult(page) { StatusCode = gated ? PostWrites.Status(reason) : StatusCodes.Status400BadRequest };
    }

    private async Task<TangentSpaceSettingsPage> Page(Capabilities place, string? personId, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var here = baseUrl.PathBase + Path;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        return new TangentSpaceSettingsPage(
            [new SettingsSection(General, baseAddress + Path, true)],
            space.Name,
            space.Description,
            baseAddress + Path,
            FormToken.For(HttpContext),
            place.Can,
            place.Gate(Bit.ManageSpace),
            personId is null ? baseAddress + Participants.SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct));
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
