using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>The place's settings, in sections shown one at a time, each listed in the rail (<see cref="Sections"/>).
/// <c>/settings</c> is general, which names and describes the place; <c>/settings/admission</c> sets how a newcomer
/// arrives; the applications are <see cref="ApplicationsController"/>'s. Each <c>GET</c> is the section's form, one
/// resource both ways. General's <c>POST</c> takes <c>name</c> and <c>description</c>, admission's takes
/// <c>admission</c> (<c>join</c> or <c>apply</c>), as a form or a JSON object, a field left as it is when not sent, and
/// sends <see cref="ChangeTangentSpace"/>: a form is sent on, from general to the door, where the name and the description
/// show, and from admission back to admission; a JSON request reads the place (<c>200</c>); a refusal reads the form
/// again with the reason inline, or a problem with the gate. A reader for whom the place does not exist receives what an
/// address with nothing there receives.</summary>
public sealed class TangentSpaceSettingsController(KoanWebBaseUrl baseUrl, WriteLoop writer) : ControllerBase
{
    public const string Path = "/settings";

    public const string AdmissionPath = "/settings/admission";

    public const string NameField = "name";
    public const string DescriptionField = "description";
    public const string AdmissionField = "admission";

    /// <summary>The section that names and describes the place.</summary>
    public const string General = "General";

    /// <summary>The section that sets how a newcomer arrives.</summary>
    public const string AdmissionSection = "Admission";

    /// <summary>The section where applications are decided, listed for whoever holds <see cref="Bit.ManageStanding"/>.</summary>
    public const string ApplicationsSection = "Applications";

    /// <summary>The admissions a form offers, in its order.</summary>
    public static readonly IReadOnlyList<string> Admissions = [Word(Admission.Join), Word(Admission.Apply)];

    /// <summary>The sections of the settings the reader of <paramref name="place"/> is shown, under
    /// <paramref name="baseAddress"/>, with <paramref name="current"/> marked: general and admission, and the applications
    /// for whoever holds <see cref="Bit.ManageStanding"/>.</summary>
    public static IReadOnlyList<SettingsSection> Sections(Capabilities place, string baseAddress, string current)
    {
        var sections = new List<SettingsSection>
        {
            new(General, baseAddress + Path, current == General),
            new(AdmissionSection, baseAddress + AdmissionPath, current == AdmissionSection)
        };
        if (place.Holds(Bit.ManageStanding))
            sections.Add(new SettingsSection(ApplicationsSection, baseAddress + ApplicationsController.Path, current == ApplicationsSection));
        return sections;
    }

    /// <summary>Where the account menu takes the reader of <paramref name="place"/> into the settings: general for whoever
    /// holds <see cref="Bit.ManageSpace"/>, the applications for whoever holds <see cref="Bit.ManageStanding"/> alone; null
    /// for anyone else.</summary>
    public static string? Entry(Capabilities place, string baseAddress)
        => place.Holds(Bit.ManageSpace) ? baseAddress + Path
            : place.Holds(Bit.ManageStanding) ? baseAddress + ApplicationsController.Path
            : null;

    /// <summary>The word an admission is written as, in a form and in JSON.</summary>
    public static string Word(Admission admission) => admission.ToString().ToLowerInvariant();

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
                return await Written(space, personId, BaseAddress() + "/", ct);
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

    [HttpGet(AdmissionPath)]
    public async Task<IActionResult> GetAdmission(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        return Ok(await AdmissionPage(place, personId, ct));
    }

    [HttpPost(AdmissionPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public async Task<IActionResult> ChangeAdmission(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();

        Admission? admission = null;
        var sent = fields.GetValueOrDefault(AdmissionField);
        if (sent is not null)
        {
            if (!Admissions.Contains(sent, StringComparer.Ordinal)) return await AdmissionRefused(personId, sent, TangentSpace.AdmissionRule, ct);
            admission = Enum.Parse<Admission>(sent, ignoreCase: true);
        }

        var outcome = await writer.Send(new ChangeTangentSpace(personId ?? "", Admission: admission), ct);
        switch (outcome)
        {
            case Outcome<TangentSpace>.Done { Result: var space }:
                return await Written(space, personId, BaseAddress() + AdmissionPath, ct);
            case Outcome<TangentSpace>.Refused { Reason: ChangeTangentSpace.NoPlace }:
                return PostWrites.Absent();
            case Outcome<TangentSpace>.Refused { Reason: var reason }:
                return await AdmissionRefused(personId, sent, reason, ct);
            case Outcome<TangentSpace>.Failed { Cause: var cause }:
                throw new InvalidOperationException("Changing the Tangent Space's admission failed.", cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for changing the Tangent Space's admission.");
        }
    }

    /// <summary>A refused admission: for a JSON request, the problem its reason answers as; for a form, the form again,
    /// read afresh, with what was sent chosen where it is one of the choices, and the reason above the form.</summary>
    private async Task<IActionResult> AdmissionRefused(string? personId, string? sent, string reason, CancellationToken ct)
    {
        var gated = reason != TangentSpace.AdmissionRule;
        if (PostWrites.WantsJson(Request))
            return gated ? PostWrites.Gated(reason) : new Problem(StatusCodes.Status400BadRequest, "Not written", reason).ToResult();

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        var page = await AdmissionPage(place, personId, ct);
        if (sent is not null && Admissions.Contains(sent, StringComparer.Ordinal)) page = page with { Admission = sent };
        page = gated ? page with { Gate = reason } : page with { Failure = reason };
        return new ObjectResult(page) { StatusCode = gated ? PostWrites.Status(reason) : StatusCodes.Status400BadRequest };
    }

    private async Task<AdmissionSettingsPage> AdmissionPage(Capabilities place, string? personId, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var here = baseUrl.PathBase + AdmissionPath;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        return new AdmissionSettingsPage(
            Sections(place, baseAddress, AdmissionSection),
            Word(space.Admission),
            Admissions,
            baseAddress + AdmissionPath,
            FormToken.For(HttpContext),
            place.Can,
            place.Gate(Bit.ManageSpace),
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct));
    }

    /// <summary>A change that committed: a form is sent on to <paramref name="onward"/>; a JSON request reads the place as
    /// it now stands, with the writer's <c>can</c> in it.</summary>
    private async Task<IActionResult> Written(TangentSpace space, string? personId, string onward, CancellationToken ct)
    {
        var door = BaseAddress() + "/";
        if (!PostWrites.WantsJson(Request))
        {
            Response.Headers.Location = onward;
            return StatusCode(StatusCodes.Status303SeeOther);
        }

        var place = await Capabilities.Load(personId, null, null, ct);
        return new ObjectResult(new WrittenTangentSpace(space.Name, space.Description, Word(space.Admission), door, BaseAddress() + Path, place.Can))
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
            Sections(place, baseAddress, General),
            space.Name,
            space.Description,
            baseAddress + Path,
            FormToken.For(HttpContext),
            place.Can,
            place.Gate(Bit.ManageSpace),
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct));
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
