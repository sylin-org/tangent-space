using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;

namespace Tangent.Space;

/// <summary>What changing the place's name and audience sends, as a form or one JSON object.</summary>
public sealed record ChangeSpaceInput(
    [Description(Holds.PlaceName)] string? Name = null,
    [Description(Holds.PlaceAudience)] string? Audience = null);

/// <summary>What setting how a newcomer arrives sends, as a form or one JSON object.</summary>
public sealed record ChangeAdmissionInput([Description(Holds.Admission)] string? Admission = null);

/// <summary>What setting the place's appearance sends, as a form or one JSON object.</summary>
public sealed record ChangeAppearanceInput(
    [Description(Holds.Base)] string? Base = null,
    [Description(Holds.Palette)] string? Palette = null,
    [Description(Holds.Css)] string? Css = null,
    [Description(Holds.PlaceImage)] string? Image = null,
    [Description(Holds.PlaceDescription)] string? Description = null,
    [Description(Holds.Intro)] string? Intro = null,
    [Description(Holds.Masthead)] string? Masthead = null,
    [Description(Holds.Footer)] string? Footer = null,
    [Description(Holds.ArticleHeader)] string? ArticleHeader = null);

/// <summary>The place's settings, in sections shown one at a time, each listed in the rail for whoever may use it
/// (<see cref="Sections"/>). <c>/settings</c> is general, which names the place and says who reads it; <c>/settings/admission</c> sets how a newcomer
/// arrives; <c>/settings/appearance</c> sets the place's appearance, its description among its words, and
/// <c>/settings/appearance/image</c> takes an image of the proprietor's own as the place's; <see cref="AppearancePreviewController"/> draws
/// the door in an appearance sent to it, saving nothing; the applications are <see cref="ApplicationsController"/>'s, the people <see cref="PeopleController"/>'s,
/// the roles <see cref="RolesController"/>'s and export <see cref="Lifecycle.ExportController"/>'s. Each <c>GET</c> is the section's form, one resource both ways. General's
/// <c>POST</c> takes <c>name</c> and <c>audience</c> (<c>public</c> or <c>private</c>), admission's
/// takes <c>admission</c> (<c>join</c> or <c>apply</c>), appearance's takes <c>base</c> (<c>tangent</c> or a bundled stylesheet), <c>palette</c> (<c>base</c>, <c>gold</c>, <c>jade</c>, <c>iris</c> or <c>copper</c>), <c>css</c>, <c>image</c> and the words in Markdown, <c>description</c>, <c>intro</c>, <c>masthead</c>, <c>footer</c> and <c>articleHeader</c>, as a form or a JSON object, a field left as it is when not sent, and
/// sends <see cref="ChangeTangentSpace"/>: a form is sent on, from general to the door, where the name
/// shows, and from admission and appearance back to themselves; a JSON request reads the place (<c>200</c>); a refusal reads the form
/// again with the reason inline, or a problem with its word. A person who may not change these but may use another
/// section opens that one instead. A reader for whom the place does not exist receives what an
/// address with nothing there receives.</summary>
[Managing]
public sealed class TangentSpaceSettingsController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Path = "/settings";

    public const string AdmissionPath = "/settings/admission";

    public const string AppearancePath = "/settings/appearance";

    public const string ImagePath = "/settings/appearance/image";

    public const string NameField = "name";
    public const string DescriptionField = "description";
    public const string AdmissionField = "admission";
    public const string AudienceField = "audience";
    public const string BaseField = "base";
    public const string PaletteField = "palette";
    public const string CssField = "css";
    public const string ImageField = "image";
    public const string IntroField = "intro";
    public const string MastheadField = "masthead";
    public const string FooterField = "footer";
    public const string ArticleHeaderField = "articleHeader";

    /// <summary>The field the place's own image comes in as, a file of a multipart form.</summary>
    public const string FileField = "file";

    /// <summary>The form that changes the place's name and audience, under <paramref name="baseAddress"/>.</summary>
    public static Form GeneralForm(string baseAddress) => Saving(Form.Posting(baseAddress + Path,
        new Form.Field(NameField, Holds.PlaceName) { Label = "Name", Limit = TangentSpace.NameLimit, Required = true },
        new Form.Field(AudienceField, Holds.PlaceAudience, As: Form.Input.Choice)
        {
            Label = "Who reads it",
            Choices =
            [
                new(Tangents.TangentWrites.Word(Audience.Public), "Public", "Anyone reads it, signed in or not, as each Tangent’s audience allows."),
                new(Tangents.TangentWrites.Word(Audience.Private), "Private", "Only its participants read it; a visitor finds the sign-in, and search engines index nothing.")
            ]
        }), "change-settings", baseAddress) with { Key = "space-settings" };

    /// <summary>A settings form as its page draws it: named for its tool, saving with the way back to the door beside it.</summary>
    private static Form Saving(Form form, string tool, string baseAddress)
        => form with { Tool = tool, Submit = "Save", Busy = "Saving…", Island = "submitting", Cancel = new Onward("Back to the door", baseAddress + "/") };

    /// <summary>The form that sets how a newcomer arrives, under <paramref name="baseAddress"/>.</summary>
    public static Form AdmissionForm(string baseAddress) => Saving(Form.Posting(baseAddress + AdmissionPath,
        new Form.Field(AdmissionField, Holds.Admission, As: Form.Input.Choice)
        {
            Label = "How a newcomer arrives",
            Choices =
            [
                new(Word(Admission.Join), "Join at once", "Whoever signs in takes part at once: they start topics and reply."),
                new(Word(Admission.Apply), "Apply and wait", "Whoever signs in applies, reads what a visitor may, and takes part once someone admits them.")
            ]
        }), "change-admission", baseAddress) with { Key = "space-admission" };

    /// <summary>The form that sets the place's appearance, under <paramref name="baseAddress"/>.</summary>
    public static Form AppearanceForm(string baseAddress) => Saving(Form.Posting(baseAddress + AppearancePath,
        new Form.Field(ImageField, Holds.PlaceImage, As: Form.Input.Picture) { Label = "The place’s image" },
        Words(DescriptionField, Holds.PlaceDescription, "Description", 2, TangentSpace.DescriptionLimit,
            "Under the place’s name on the door. Its words are what a link to the door says of it."),
        Words(IntroField, Holds.Intro, "Welcome", 5, Appearance.ProseLimit,
            "Under the door’s head, for whoever the place exists for: what this place is for and how to begin."),
        new Form.Field(PaletteField, Holds.Palette, As: Form.Input.Choice)
        {
            Label = "The colours",
            Note = "Links, buttons and what is new take the accent on every page, for everyone. Light or dark is each reader’s own, at the foot of any page."
        },
        new Form.Field(BaseField, Holds.Base, As: Form.Input.Choice) { Label = "The base: the plain elements and the fonts" },
        Words(MastheadField, Holds.Masthead, "Masthead line", 2, Appearance.LineLimit, "Beside the place’s name on every page.") with { Group = Lines, Default = "" },
        Words(FooterField, Holds.Footer, "Footer line", 2, Appearance.LineLimit, "First in every page’s footer.") with { Group = Lines, Default = "" },
        Words(ArticleHeaderField, Holds.ArticleHeader, "Article header", 3, Appearance.ProseLimit,
            "Above every article’s title.", " Each is Markdown, written as a Post is. An image comes from another site or from under `/media/` here. The pages of an export and of the archive carry them.")
            with { Group = Lines, Default = "" },
        new Form.Field(CssField, Holds.Css, As: Form.Input.Area)
        {
            Label = "Your own CSS", Rows = 8, Limit = Appearance.CssLimit, Exact = true, Group = Lines, Default = "",
            Note = "Over every other layer, on every page but these settings. Pages say what each thing is: `article.post.by-agent > header`, `.row.topic.is-new`, `.tangent-head.is-pictured`. At most "
                   + Appearance.CssLimit.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " characters."
        }), "change-appearance", baseAddress) with { Key = "space-appearance" };

    /// <summary>The line the place's lines, its article header and its own CSS sit behind.</summary>
    private const string Lines = "Lines, the article header and your own CSS";

    /// <summary>One of the place's words: its Markdown, where it shows and how much it holds.</summary>
    private static Form.Field Words(string name, string holds, string label, int rows, int limit, string note, string after = "") => new(name, holds, As: Form.Input.Area)
    {
        Label = label, Rows = rows, Limit = limit,
        Note = note + " Markdown, at most " + limit.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " characters." + after
    };

    /// <summary>The section that names the place and says who reads it.</summary>
    public const string General = "General";

    /// <summary>The section that sets how a newcomer arrives.</summary>
    public const string AdmissionSection = "Admission";

    /// <summary>The section where applications are decided, listed for whoever holds <see cref="Bit.ManageStanding"/>.</summary>
    public const string ApplicationsSection = "Applications";

    /// <summary>The section that sets the place's appearance and its words.</summary>
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
    [Description("Read the place's name and audience, with the sections of its settings you may use. Call this before change_settings.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = Here.Of(HttpContext).Around;
        return Elsewhere(place) ?? Ok(await Page(place, personId, ct));
    }

    [HttpPost(Path)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "change_settings", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.ManageSpace)]
    [Description("Change the place's name or audience, for whoever holds ManageSpace. What you send replaces what stood, and a private place serves a visitor only the sign-in. Its description is change_appearance's.")]
    public async Task<IActionResult> Change(ChangeSpaceInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = Here.Of(HttpContext).PersonId;

        Audience? audience = null;
        if (input.Audience is { } sent)
        {
            if (!Audiences.Contains(sent, StringComparer.Ordinal)) return await Refused(personId, input, TangentSpace.AudienceRule, ct);
            audience = Enum.Parse<Audience>(sent, ignoreCase: true);
        }

        var outcome = await new ChangeTangentSpace(personId ?? "", input.Name, Audience: audience).Send();
        switch (outcome)
        {
            case Outcome<TangentSpace>.Done { Result: var space }:
                return await Written(space, personId, BaseAddress() + "/", ct);
            case Outcome<TangentSpace>.Refused { Reason: { Kind: not ReasonKind.Absent } reason }:
                return await Refused(personId, input, reason, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    [HttpGet(AdmissionPath)]
    [Tool(Name = "read_admission")]
    [Description("Read how a newcomer arrives here. Call this before change_admission.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> GetAdmission(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = Here.Of(HttpContext).Around;
        return Elsewhere(place) ?? Ok(await AdmissionPage(place, personId, ct));
    }

    [HttpPost(AdmissionPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "change_admission", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Set how a newcomer arrives, joining at once or applying and waiting, for whoever holds ManageSpace. Everyone already here stays as they are.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> ChangeAdmission(ChangeAdmissionInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = Here.Of(HttpContext).Around;

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
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>A refused admission: for a JSON request, the problem its reason answers as; for a form, the form again,
    /// read afresh, with what was sent chosen where it is one of the choices, and the reason above the form.</summary>
    private async Task<IActionResult> AdmissionRefused(string? personId, string? sent, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);
        var page = await AdmissionPage(place, personId, ct);
        if (sent is not null && Admissions.Contains(sent, StringComparer.Ordinal)) page = page with { Admission = sent };
        page = reason.Kind == ReasonKind.Gated ? page with { Gate = reason.Word, Change = null } : page with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
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
    [Description("Read the place's appearance — its base, its colours, its own CSS, its image and its words in Markdown: the description, the welcome under the door's head, the masthead and footer lines and the article header. Call this before change_appearance.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> GetAppearance(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = Here.Of(HttpContext).Around;
        return Elsewhere(place) ?? Ok(await AppearancePage(place, personId, ct));
    }

    [HttpPost(AppearancePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "change_appearance", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Set how the place looks to every reader on every page, for whoever holds ManageSpace: the base layer's stylesheet, the colours, the place's own CSS, which every page but the settings carries over the rest, its image, and its words in Markdown. Sending a part its default returns it there: base tangent, palette base, anything else empty. What you leave out stays.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> ChangeAppearance(ChangeAppearanceInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = Here.Of(HttpContext).Around;

        var outcome = await new ChangeTangentSpace(personId ?? "", Description: input.Description, Base: input.Base, Palette: input.Palette, Css: input.Css,
            Image: input.Image, Masthead: input.Masthead, Footer: input.Footer, Intro: input.Intro, ArticleHeader: input.ArticleHeader).Send();
        switch (outcome)
        {
            case Outcome<TangentSpace>.Done { Result: var space }:
                return await Written(space, personId, BaseAddress() + AppearancePath, ct);
            case Outcome<TangentSpace>.Refused { Reason: { Kind: not ReasonKind.Absent } reason }:
                return await AppearanceRefused(personId, input, reason, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>An image of the proprietor's own, sent as the file of a multipart form, kept as every image is
    /// (<see cref="Media.Keep"/>) and worn at once as the place's; the form is sent back to the appearance.</summary>
    [HttpPost(ImagePath)]
    [RequestSizeLimit(NewMediaController.RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = NewMediaController.RequestLimit)]
    public async Task<IActionResult> TakeImage(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);
        if (personId is null || place.Gate(Bit.ManageSpace) is not null) return Answers.Refused(HttpContext, place.Gate(Bit.ManageSpace) ?? Capabilities.SignIn);

        var file = Request.HasFormContentType ? (await Request.ReadFormAsync(ct)).Files.GetFile(FileField) : null;
        if (file is null) return await AppearanceRefused(personId, new ChangeAppearanceInput(), Media.NotAnImage, ct);
        Media media;
        try
        {
            if (file.Length > Media.SentLimit) throw new Refusal(Media.TooLarge);
            using var bytes = new MemoryStream();
            await file.CopyToAsync(bytes, ct);
            media = await Media.Keep(bytes.ToArray(), ct);
        }
        catch (Refusal refusal)
        {
            return await AppearanceRefused(personId, new ChangeAppearanceInput(), refusal.Reason, ct);
        }

        await new BringMedia(media.Id, personId).Send();
        var outcome = await new ChangeTangentSpace(personId, Image: Media.Reference(media.Id)).Send();
        return outcome switch
        {
            Outcome<TangentSpace>.Done { Result: var space } => await Written(space, personId, BaseAddress() + AppearancePath, ct),
            Outcome<TangentSpace>.Refused { Reason: { Kind: not ReasonKind.Absent } reason } => await AppearanceRefused(personId, new ChangeAppearanceInput(), reason, ct),
            _ => Answers.NotDone(HttpContext, outcome)
        };
    }

    /// <summary>A refused appearance: for a JSON request, the problem its reason answers as; for a form, the form again, read
    /// afresh, with each choice sent chosen where it is one of the choices, the words and the CSS as they were sent, and the
    /// reason above the form.</summary>
    private async Task<IActionResult> AppearanceRefused(string? personId, ChangeAppearanceInput input, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);
        var page = await AppearancePage(place, personId, ct);
        page = page with
        {
            Base = input.Base is { } sentBase && Appearance.Bases.Contains(sentBase, StringComparer.Ordinal) ? sentBase : page.Base,
            Palette = input.Palette is { } sentPalette && Appearance.Palettes.Contains(sentPalette, StringComparer.Ordinal) ? sentPalette : page.Palette,
            Image = input.Image is { } sentImage && Art.Judged(sentImage) is { } image ? image : page.Image,
            Css = input.Css ?? page.Css,
            Description = input.Description ?? page.Description,
            Intro = input.Intro ?? page.Intro,
            Masthead = input.Masthead ?? page.Masthead,
            Footer = input.Footer ?? page.Footer,
            ArticleHeader = input.ArticleHeader ?? page.ArticleHeader
        };
        page = reason.Kind == ReasonKind.Gated ? page with { Gate = reason.Word, Change = null } : page with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    private async Task<AppearanceSettingsPage> AppearancePage(Capabilities place, string? personId, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var here = baseUrl.PathBase + AppearancePath;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        var mutable = place.Gate(Bit.ManageSpace) is null;
        var appearance = space.Appearance;
        return new AppearanceSettingsPage(
            Sections(place, baseAddress, AppearanceSection),
            Appearance.Word(appearance.Base),
            Appearance.Bases,
            Appearance.Word(appearance.Palette),
            Appearance.Palettes,
            appearance.Css,
            appearance.Image,
            appearance.ImageAt(baseAddress + "/"),
            Art.Covers.Select(cover => new AppearanceSettingsPage.Cover(cover.Name, cover.Title, baseAddress + "/" + Art.File(cover.Name))).ToList(),
            space.Description,
            appearance.Intro,
            appearance.Masthead,
            appearance.Footer,
            appearance.ArticleHeader,
            mutable ? AppearanceForm(baseAddress) : null,
            mutable ? baseAddress + AppearancePreviewController.Path : null,
            mutable ? baseAddress + ImagePath : null,
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
            Appearance.Word(space.Appearance.Base), Appearance.Word(space.Appearance.Palette), space.Appearance.Css, space.Appearance.Image,
            space.Appearance.Intro, space.Appearance.Masthead, space.Appearance.Footer, space.Appearance.ArticleHeader, door, BaseAddress() + Path, place.Can))
        {
            ContentTypes = { "application/json" }
        };
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again, read afresh,
    /// keeping what was sent, with the reason where it belongs.</summary>
    private async Task<IActionResult> Refused(string? personId, ChangeSpaceInput input, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);
        var page = await Page(place, personId, ct);
        page = page with
        {
            Name = input.Name ?? page.Name,
            Audience = input.Audience is { } audience && Audiences.Contains(audience, StringComparer.Ordinal) ? audience : page.Audience
        };
        page = reason.Kind == ReasonKind.Gated ? page with { Gate = reason.Word, Change = null }
            : reason == TangentSpace.NameRule ? page with { NameFailure = reason.Text }
            : page with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    private async Task<TangentSpaceSettingsPage> Page(Capabilities place, string? personId, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var here = baseUrl.PathBase + Path;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        return new TangentSpaceSettingsPage(
            Sections(place, baseAddress, General),
            space.Name,
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
