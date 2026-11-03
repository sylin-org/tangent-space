using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;

namespace Tangent.Space;

/// <summary>What changing the place's name, description and audience sends, as a form or one JSON object.</summary>
public sealed record ChangeSpaceInput(
    [Description(Holds.PlaceName)] string? Name = null,
    [Description(Holds.PlaceDescription)] string? Description = null,
    [Description(Holds.PlaceAudience)] string? Audience = null);

/// <summary>What setting how a newcomer arrives sends, as a form or one JSON object.</summary>
public sealed record ChangeAdmissionInput([Description(Holds.Admission)] string? Admission = null);

/// <summary>What setting the place's accent and slots sends, as a form or one JSON object.</summary>
public sealed record ChangeAppearanceInput(
    [Description(Holds.Accent)] string? Accent = null,
    [Description(Holds.HeadSlot)] string? Head = null,
    [Description(Holds.MastheadSlot)] string? Masthead = null,
    [Description(Holds.DoorIntroSlot)] string? DoorIntro = null,
    [Description(Holds.ArticleHeaderSlot)] string? ArticleHeader = null,
    [Description(Holds.FooterSlot)] string? Footer = null);

/// <summary>The place's settings, in sections shown one at a time, each listed in the rail for whoever may use it
/// (<see cref="Sections"/>). <c>/settings</c> is general, which names and describes the place; <c>/settings/admission</c> sets how a newcomer
/// arrives; <c>/settings/appearance</c> sets the place's accent and its slots; the applications are <see cref="ApplicationsController"/>'s, the people <see cref="PeopleController"/>'s,
/// the roles <see cref="RolesController"/>'s and export <see cref="Lifecycle.ExportController"/>'s. Each <c>GET</c> is the section's form, one resource both ways. General's
/// <c>POST</c> takes <c>name</c>, <c>description</c> and <c>audience</c> (<c>public</c> or <c>private</c>), admission's
/// takes <c>admission</c> (<c>join</c> or <c>apply</c>), appearance's takes <c>accent</c> (<c>gold</c>, <c>jade</c>, <c>iris</c> or <c>copper</c>) and the slots <c>head</c>, <c>masthead</c>, <c>doorIntro</c>, <c>articleHeader</c> and <c>footer</c>, as a form or a JSON object, a field left as it is when not sent, and
/// sends <see cref="ChangeTangentSpace"/>: a form is sent on, from general to the door, where the name and the description
/// show, and from admission and appearance back to themselves; a JSON request reads the place (<c>200</c>); a refusal reads the form
/// again with the reason inline, or a problem with the gate. A person who may not change these but may use another
/// section opens that one instead. A reader for whom the place does not exist receives what an
/// address with nothing there receives.</summary>
public sealed class TangentSpaceSettingsController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Path = "/settings";

    public const string AdmissionPath = "/settings/admission";

    public const string AppearancePath = "/settings/appearance";

    public const string NameField = "name";
    public const string DescriptionField = "description";
    public const string AdmissionField = "admission";
    public const string AudienceField = "audience";
    public const string AccentField = "accent";
    public const string HeadField = "head";
    public const string MastheadField = "masthead";
    public const string DoorIntroField = "doorIntro";
    public const string ArticleHeaderField = "articleHeader";
    public const string FooterField = "footer";

    /// <summary>The form that changes the place's name, description and audience, under <paramref name="baseAddress"/>.</summary>
    private static Form GeneralForm(string baseAddress) => Form.Posting(baseAddress + Path,
        new Form.Field(NameField, Holds.PlaceName),
        new Form.Field(DescriptionField, Holds.PlaceDescription),
        new Form.Field(AudienceField, Holds.PlaceAudience));

    /// <summary>The form that sets how a newcomer arrives, under <paramref name="baseAddress"/>.</summary>
    private static Form AdmissionForm(string baseAddress) => Form.Posting(baseAddress + AdmissionPath,
        new Form.Field(AdmissionField, Holds.Admission));

    /// <summary>The form that sets the place's accent and its slots, under <paramref name="baseAddress"/>.</summary>
    private static Form AppearanceForm(string baseAddress) => Form.Posting(baseAddress + AppearancePath,
        new Form.Field(AccentField, Holds.Accent),
        new Form.Field(HeadField, Holds.HeadSlot),
        new Form.Field(MastheadField, Holds.MastheadSlot),
        new Form.Field(DoorIntroField, Holds.DoorIntroSlot),
        new Form.Field(ArticleHeaderField, Holds.ArticleHeaderSlot),
        new Form.Field(FooterField, Holds.FooterSlot));

    /// <summary>The section that names and describes the place.</summary>
    public const string General = "General";

    /// <summary>The section that sets how a newcomer arrives.</summary>
    public const string AdmissionSection = "Admission";

    /// <summary>The section where applications are decided, listed for whoever holds <see cref="Bit.ManageStanding"/>.</summary>
    public const string ApplicationsSection = "Applications";

    /// <summary>The section that sets the place's accent and its slots.</summary>
    public const string AppearanceSection = "Appearance";

    /// <summary>The section where the place's people are found, their standing changed and their roles given and taken,
    /// listed for whoever holds <see cref="Bit.ManageStanding"/> or <see cref="Bit.ManageRoles"/>.</summary>
    public const string PeopleSection = "People";

    /// <summary>The section where the place's roles are created, changed, ordered and deleted, listed for whoever holds
    /// <see cref="Bit.ManageRoles"/>.</summary>
    public const string RolesSection = "Roles";

    /// <summary>The section where the place is taken along, one Tangent or all of it, listed for whoever holds
    /// <see cref="Bit.ManageSpace"/>.</summary>
    public const string ExportSection = "Export";

    /// <summary>The admissions a form offers, in its order.</summary>
    public static readonly IReadOnlyList<string> Admissions = [Word(Admission.Join), Word(Admission.Apply)];

    /// <summary>The audiences a form offers, in its order.</summary>
    public static readonly IReadOnlyList<string> Audiences = [Tangents.TangentWrites.Word(Audience.Public), Tangents.TangentWrites.Word(Audience.Private)];

    /// <summary>The sections of the settings the reader of <paramref name="place"/> may use, under
    /// <paramref name="baseAddress"/>, with <paramref name="current"/> marked: general, admission and appearance for whoever
    /// holds <see cref="Bit.ManageSpace"/>; the applications for whoever holds <see cref="Bit.ManageStanding"/>; the people for
    /// whoever holds it or <see cref="Bit.ManageRoles"/>; the roles for whoever holds <see cref="Bit.ManageRoles"/>; and
    /// export for whoever holds <see cref="Bit.ManageSpace"/>.
    /// A reader who may use none has none.</summary>
    public static IReadOnlyList<SettingsSection> Sections(Capabilities place, string baseAddress, string current)
    {
        var sections = new List<SettingsSection>();
        if (place.Holds(Bit.ManageSpace))
        {
            sections.Add(new SettingsSection(General, baseAddress + Path, current == General));
            sections.Add(new SettingsSection(AdmissionSection, baseAddress + AdmissionPath, current == AdmissionSection));
        }
        if (place.Holds(Bit.ManageStanding))
            sections.Add(new SettingsSection(ApplicationsSection, baseAddress + ApplicationsController.Path, current == ApplicationsSection));
        if (place.Holds(Bit.ManageSpace))
            sections.Add(new SettingsSection(AppearanceSection, baseAddress + AppearancePath, current == AppearanceSection));
        if (PeopleController.ReadGate(place) is null)
            sections.Add(new SettingsSection(PeopleSection, baseAddress + PeopleController.Path, current == PeopleSection));
        if (place.Holds(Bit.ManageRoles))
            sections.Add(new SettingsSection(RolesSection, baseAddress + RolesController.Path, current == RolesSection));
        if (place.Holds(Bit.ManageSpace))
            sections.Add(new SettingsSection(ExportSection, baseAddress + Lifecycle.ExportController.Path, current == ExportSection));
        return sections;
    }

    /// <summary>Where the reader of <paramref name="place"/> enters the settings: the first section they may use; null for
    /// whoever may use none.</summary>
    public static string? Entry(Capabilities place, string baseAddress)
        => Sections(place, baseAddress, General).FirstOrDefault()?.Address;

    /// <summary>A person who may not change general, admission or appearance but may use another section, sent to the first they may
    /// use; null for anyone else, who reads the section as it is, and for an agent, whose JSON says the gate and the
    /// sections it may use.</summary>
    private IActionResult? Elsewhere(Capabilities place)
    {
        if (place.Holds(Bit.ManageSpace) || PostWrites.WantsJson(Request) || Entry(place, BaseAddress()) is not { } first) return null;
        Response.Headers.Location = first;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    /// <summary>The word an admission is written as, in a form and in JSON.</summary>
    public static string Word(Admission admission) => admission.ToString().ToLowerInvariant();

    [HttpGet(Path)]
    [Tool(Name = "read_settings")]
    [Description("Read the place's name, description and audience, with the sections of its settings you may use. Call this before change_settings.")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(HttpContext, place.Gate(Bit.View));
        return Elsewhere(place) ?? Ok(await Page(place, personId, ct));
    }

    [HttpPost(Path)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "change_settings", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Change the place's name, description or audience, for whoever holds ManageSpace. What you send replaces what stood, and a private place serves a visitor only the sign-in.")]
    public async Task<IActionResult> Change(ChangeSpaceInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(HttpContext, place.Gate(Bit.View));

        Audience? audience = null;
        if (input.Audience is { } sent)
        {
            if (!Audiences.Contains(sent, StringComparer.Ordinal)) return await Refused(personId, input, TangentSpace.AudienceRule, ct);
            audience = Enum.Parse<Audience>(sent, ignoreCase: true);
        }

        var outcome = await new ChangeTangentSpace(personId ?? "", input.Name, input.Description, Audience: audience).Send();
        switch (outcome)
        {
            case Outcome<TangentSpace>.Done { Result: var space }:
                return await Written(space, personId, BaseAddress() + "/", ct);
            case Outcome<TangentSpace>.Refused { Reason: { Kind: not ReasonKind.Absent } reason }:
                return await Refused(personId, input, reason, ct);
            default:
                return PostWrites.Answer(HttpContext, outcome);
        }
    }

    [HttpGet(AdmissionPath)]
    [Tool(Name = "read_admission")]
    [Description("Read how a newcomer arrives here. Call this before change_admission.")]
    public async Task<IActionResult> GetAdmission(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(HttpContext, place.Gate(Bit.View));
        return Elsewhere(place) ?? Ok(await AdmissionPage(place, personId, ct));
    }

    [HttpPost(AdmissionPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "change_admission", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Set how a newcomer arrives, joining at once or applying and waiting, for whoever holds ManageSpace. Everyone already here stays as they are.")]
    public async Task<IActionResult> ChangeAdmission(ChangeAdmissionInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(HttpContext, place.Gate(Bit.View));

        Admission? admission = null;
        var sent = input.Admission;
        if (sent is not null)
        {
            if (!Admissions.Contains(sent, StringComparer.Ordinal)) return await AdmissionRefused(personId, sent, TangentSpace.AdmissionRule, ct);
            admission = Enum.Parse<Admission>(sent, ignoreCase: true);
        }

        var outcome = await new ChangeTangentSpace(personId ?? "", Admission: admission).Send();
        switch (outcome)
        {
            case Outcome<TangentSpace>.Done { Result: var space }:
                return await Written(space, personId, BaseAddress() + AdmissionPath, ct);
            case Outcome<TangentSpace>.Refused { Reason: { Kind: not ReasonKind.Absent } reason }:
                return await AdmissionRefused(personId, sent, reason, ct);
            default:
                return PostWrites.Answer(HttpContext, outcome);
        }
    }

    /// <summary>A refused admission: for a JSON request, the problem its reason answers as; for a form, the form again,
    /// read afresh, with what was sent chosen where it is one of the choices, and the reason above the form.</summary>
    private async Task<IActionResult> AdmissionRefused(string? personId, string? sent, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return PostWrites.Refused(HttpContext, reason);

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(HttpContext, place.Gate(Bit.View));
        var page = await AdmissionPage(place, personId, ct);
        if (sent is not null && Admissions.Contains(sent, StringComparer.Ordinal)) page = page with { Admission = sent };
        page = reason.Kind == ReasonKind.Gated ? page with { Gate = reason.Word, Change = null } : page with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = PostWrites.Status(reason) };
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
            place.Gate(Bit.ManageSpace) is null ? AdmissionForm(baseAddress) : null,
            place.Can,
            place.Gate(Bit.ManageSpace)?.Word,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct));
    }

    [HttpGet(AppearancePath)]
    [Tool(Name = "read_appearance")]
    [Description("Read the place's accent and slots. Call this before change_appearance.")]
    public async Task<IActionResult> GetAppearance(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(HttpContext, place.Gate(Bit.View));
        return Elsewhere(place) ?? Ok(await AppearancePage(place, personId, ct));
    }

    [HttpPost(AppearancePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "change_appearance", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Set the place's accent and slots, which every page shows every reader, for whoever holds ManageSpace. A slot you send replaces what it held; one you leave out stays.")]
    public async Task<IActionResult> ChangeAppearance(ChangeAppearanceInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(HttpContext, place.Gate(Bit.View));

        Accent? accent = null;
        if (input.Accent is { } sent)
        {
            if (!Appearance.Accents.Contains(sent, StringComparer.Ordinal)) return await AppearanceRefused(personId, input, TangentSpace.AccentRule, ct);
            accent = Enum.Parse<Accent>(sent, ignoreCase: true);
        }

        var outcome = await new ChangeTangentSpace(personId ?? "", Accent: accent, Head: input.Head, Masthead: input.Masthead, DoorIntro: input.DoorIntro,
            ArticleHeader: input.ArticleHeader, Footer: input.Footer).Send();
        switch (outcome)
        {
            case Outcome<TangentSpace>.Done { Result: var space }:
                return await Written(space, personId, BaseAddress() + AppearancePath, ct);
            case Outcome<TangentSpace>.Refused { Reason: { Kind: not ReasonKind.Absent } reason }:
                return await AppearanceRefused(personId, input, reason, ct);
            default:
                return PostWrites.Answer(HttpContext, outcome);
        }
    }

    /// <summary>A refused appearance: for a JSON request, the problem its reason answers as; for a form, the form again, read
    /// afresh, with the accent sent chosen where it is one of the choices, the slots as they were sent, and the reason above
    /// the form.</summary>
    private async Task<IActionResult> AppearanceRefused(string? personId, ChangeAppearanceInput input, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return PostWrites.Refused(HttpContext, reason);

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(HttpContext, place.Gate(Bit.View));
        var page = await AppearancePage(place, personId, ct);
        if (input.Accent is { } sent && Appearance.Accents.Contains(sent, StringComparer.Ordinal)) page = page with { Accent = sent };
        page = page with
        {
            Slots = page.Slots with
            {
                Head = input.Head ?? page.Slots.Head,
                Masthead = input.Masthead ?? page.Slots.Masthead,
                DoorIntro = input.DoorIntro ?? page.Slots.DoorIntro,
                ArticleHeader = input.ArticleHeader ?? page.Slots.ArticleHeader,
                Footer = input.Footer ?? page.Slots.Footer
            }
        };
        page = reason.Kind == ReasonKind.Gated ? page with { Gate = reason.Word, Change = null } : page with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = PostWrites.Status(reason) };
    }

    private async Task<AppearanceSettingsPage> AppearancePage(Capabilities place, string? personId, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var here = baseUrl.PathBase + AppearancePath;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        return new AppearanceSettingsPage(
            Sections(place, baseAddress, AppearanceSection),
            Appearance.Word(space.Appearance.Accent),
            Appearance.Accents,
            space.Slots,
            place.Gate(Bit.ManageSpace) is null ? AppearanceForm(baseAddress) : null,
            place.Can,
            place.Gate(Bit.ManageSpace)?.Word,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct));
    }

    /// <summary>A change that committed: a form is sent on to <paramref name="onward"/>; a JSON request reads the place as
    /// it now stands, with the sender's <c>can</c> in it.</summary>
    private async Task<IActionResult> Written(TangentSpace space, string? personId, string onward, CancellationToken ct)
    {
        var door = BaseAddress() + "/";
        if (!PostWrites.WantsJson(Request))
        {
            Response.Headers.Location = onward;
            return StatusCode(StatusCodes.Status303SeeOther);
        }

        var place = await Capabilities.Load(personId, null, null, ct);
        return new ObjectResult(new WrittenTangentSpace(space.Name, space.Description, Word(space.Admission), Tangents.TangentWrites.Word(space.Audience),
            Appearance.Word(space.Appearance.Accent), space.Slots, door, BaseAddress() + Path, place.Can))
        {
            ContentTypes = { "application/json" }
        };
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again, read afresh,
    /// keeping what was sent, with the reason where it belongs.</summary>
    private async Task<IActionResult> Refused(string? personId, ChangeSpaceInput input, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return PostWrites.Refused(HttpContext, reason);

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(HttpContext, place.Gate(Bit.View));
        var page = await Page(place, personId, ct);
        page = page with
        {
            Name = input.Name ?? page.Name,
            Description = input.Description ?? page.Description,
            Audience = input.Audience is { } audience && Audiences.Contains(audience, StringComparer.Ordinal) ? audience : page.Audience
        };
        page = reason.Kind == ReasonKind.Gated ? page with { Gate = reason.Word, Change = null } : reason.Text switch
        {
            TangentSpace.NameRule => page with { NameFailure = reason.Text },
            TangentSpace.DescriptionRule => page with { DescriptionFailure = reason.Text },
            _ => page with { Failure = reason.Text }
        };
        return new ObjectResult(page) { StatusCode = PostWrites.Status(reason) };
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
            Tangents.TangentWrites.Word(space.Audience),
            Audiences,
            place.Gate(Bit.ManageSpace) is null ? GeneralForm(baseAddress) : null,
            place.Can,
            place.Gate(Bit.ManageSpace)?.Word,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct));
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
