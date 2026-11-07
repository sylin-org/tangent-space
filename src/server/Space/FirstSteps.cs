using System.ComponentModel;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Tangents;

namespace Tangent.Space;

/// <summary>Where the place's manager makes it theirs, on its door: each choice is made at the press that picks it, through
/// the write its settings send, which returns to the door. What the place is for, as the Tangents it holds
/// (<see cref="Purposes"/>); the line under its name, offered from their Bluesky profile where it says one
/// (<see cref="Line"/>); how it looks — its accent, its image, the banner and the picture their Bluesky account shows as its
/// image and its mark; who reads it and how a newcomer arrives; where its site is pointed here, the pointers in the general
/// settings (<see cref="Site"/>); and the first Topic. <see cref="Close"/> ends them for good, and whatever they set, the
/// settings change later.</summary>
public sealed record FirstSteps(
    IReadOnlyList<FirstSteps.Purpose> Purposes,
    Form? Line,
    string Describe,
    Form Accent,
    Form Image,
    Form? Banner,
    Form? Mark,
    string Looks,
    Form Readers,
    Form Arrival,
    string Site,
    string? FirstTopic,
    Form Close) : IPart
{
    /// <summary>Who the steps greet: the first word of the name the place shows them.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string Greeted { get; init; } = "";

    /// <summary>The line the place says under its name now, where it says one.</summary>
    public string? Described { get; init; }

    /// <summary>The line their Bluesky profile says, which <see cref="Line"/> makes the place's.</summary>
    public string? Said { get; init; }

    /// <summary>A Tangent the place may be for: its name and what it holds, and either its address and the one press that
    /// deletes it, empty as it is while the steps stand, where the place has it, or the one press that creates it.</summary>
    public sealed record Purpose(string Name, string Detail, string? Address, Form? Add, Form? Delete = null);

    /// <summary>The Tangents first steps offer, in their order: the first Tangent first run makes, then the kinds of
    /// place the brief names.</summary>
    private static readonly (string Name, string Description, PostKind Kind, Audience Audience, string Detail)[] Offered =
    [
        (FirstTangent.Name, "Open questions and conversations.", PostKind.Topic, Audience.Public, "Conversations, open to everyone"),
        ("Articles", "Long pieces, published here and on Bluesky.", PostKind.Article, Audience.Public, "Long pieces you publish"),
        ("The workshop", "Code, experiments and work in progress.", PostKind.Topic, Audience.Public, "Code and experiments"),
        ("Off the record", "For whoever is let in, and nobody else.", PostKind.Topic, Audience.Private, "Private, for whoever you let in")
    ];

    /// <summary>How many of the bundled covers the steps offer; the rest are in the settings.</summary>
    public const int CoverLimit = 5;

    /// <summary>The steps for <paramref name="reader"/>, shown as <paramref name="shown"/>: present only where they hold
    /// <see cref="Bit.ManageSpace"/>, the place's manager has not closed them, and no Tangent but the moderators' holds a
    /// Topic, judged from the Tangents the door already read (<paramref name="listed"/>, all of them unless
    /// <paramref name="more"/>).</summary>
    public static FirstSteps? For(Capabilities place, TangentSpace space, IReadOnlyList<Tangents.Tangent> listed, bool more, Participant? reader, string shown,
        string baseAddress)
    {
        if (reader is null || !place.Holds(Bit.ManageSpace) || more || space.FirstStepsClosed) return null;
        var open = listed.Where(tangent => tangent.Id != space.ModeratorsTangentId).ToList();
        if (open.Any(tangent => tangent.TopicCount > 0)) return null;

        var door = baseAddress + "/";
        var appearance = space.Appearance;
        var settings = baseAddress + TangentSpaceSettingsController.AppearancePath;
        var profile = reader.Profile;
        var said = profile.Description?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim() is { Length: > 0 and <= TangentSpace.DescriptionLimit } line
                   && line != space.Description
            ? line
            : null;

        var purposes = Offered.Select(offered =>
        {
            var slug = Slug.From(offered.Name, NewTangentController.Unnamed);
            return open.FirstOrDefault(tangent => tangent.Id == slug) is { } held
                ? new Purpose(held.Name, offered.Detail, PostWrites.TangentAddress(baseAddress, held.Id), null,
                    Back(TangentWrites.DeleteForm(baseAddress, held.Id, held.TopicCount) with { Quiet = true }))
                : new Purpose(offered.Name, offered.Detail, null, Back(Form.Posting(baseAddress + NewTangentController.Route,
                        Given("name", Holds.TangentName, offered.Name), Given("description", Holds.TangentDescription, offered.Description),
                        Given("kind", Holds.TangentKind, PostWrites.Word(offered.Kind)), Given("audience", Holds.TangentAudience, TangentWrites.Word(offered.Audience)))
                    with { Tool = "create-tangent", Submit = "Add", Busy = "Adding…", Quiet = true }));
        }).ToList();

        List<Form.Choice> images = [new("", "None")];
        if (Media.IdIn(appearance.Image) is not null && appearance.ImageAt(door) is { } own) images.Add(new(appearance.Image, "Your own") { Picture = own });
        images.AddRange(Art.Covers.Take(CoverLimit).Select(cover => new Form.Choice(cover.Name, cover.Title) { Picture = door + Art.File(cover.Name) }));
        if (Art.Covers.FirstOrDefault(cover => cover.Name == appearance.Image) is { } worn && images.All(choice => choice.Value != worn.Name))
            images.Add(new(worn.Name, worn.Title) { Picture = door + Art.File(worn.Name) });

        return new FirstSteps(
            purposes,
            said is null ? null : Back(Form.Posting(settings, Given(TangentSpaceSettingsController.DescriptionField, Holds.PlaceDescription, said))
                with { Tool = "change-appearance", Submit = "Use it", Busy = "Saving…", Quiet = true }),
            settings + "#space-appearance-" + TangentSpaceSettingsController.DescriptionField,
            Back(Form.Posting(settings, Switch(TangentSpaceSettingsController.PaletteField, Holds.Palette, appearance.Accent,
                [.. Appearance.Palettes.Where(word => word != Appearance.Word(Palette.Base)).Select(word => new Form.Choice(word, char.ToUpperInvariant(word[0]) + word[1..]) { Swatch = word })]))
                with { Tool = "change-appearance", Key = "first-steps-accent" }),
            Back(Form.Posting(settings, Switch(TangentSpaceSettingsController.ImageField, Holds.PlaceImage, appearance.Image, images))
                with { Tool = "change-appearance", Key = "first-steps-image" }),
            profile.Banner is null ? null : Back(AppearanceSettingsPage.FromBluesky(settings, TangentSpaceSettingsController.ImageField, "Use your Bluesky banner") with { Quiet = true }),
            appearance.Mark.Length > 0
                ? Back(Form.Posting(settings, Given(TangentSpaceSettingsController.MarkField, Holds.PlaceMark, "")) with { Tool = "change-appearance", Submit = "Use its letter", Quiet = true })
                : profile.Avatar is null ? null : Back(AppearanceSettingsPage.FromBluesky(settings, TangentSpaceSettingsController.MarkField, "Use your Bluesky picture as its mark") with { Quiet = true }),
            settings,
            Back(Form.Posting(baseAddress + TangentSpaceSettingsController.Path, Switch(TangentSpaceSettingsController.AudienceField, Holds.PlaceAudience,
                TangentWrites.Word(space.Audience), [new(TangentWrites.Word(Audience.Public), "Anyone"), new(TangentWrites.Word(Audience.Private), "Only who is in")]))
                with { Tool = "change-settings", Key = "first-steps-readers" }),
            Back(Form.Posting(baseAddress + TangentSpaceSettingsController.AdmissionPath, Switch(TangentSpaceSettingsController.AdmissionField, Holds.Admission,
                TangentSpaceSettingsController.Word(space.Admission),
                [new(TangentSpaceSettingsController.Word(Admission.Join), "Join at once"), new(TangentSpaceSettingsController.Word(Admission.Apply), "Apply, and you admit them")]))
                with { Tool = "change-admission", Key = "first-steps-arrival" }),
            baseAddress + TangentSpaceSettingsController.Path + "#" + Pointing.Anchor,
            open.FirstOrDefault() is { } first ? PostWrites.TangentAddress(baseAddress, first.Id) + "/" + TopicsController.NewTopicSegment : null,
            Form.Posting(baseAddress + FirstStepsController.ClosePath) with { Tool = "close-first-steps", Submit = "Done", Busy = "Closing…", Quiet = true })
        {
            Greeted = Greeting(shown),
            Described = space.Description.Length > 0 ? space.Description : null,
            Said = said
        };
    }

    /// <summary>How the steps greet someone shown as <paramref name="shown"/>: the first word of their name, or, where the
    /// place shows them by their handle, its first label capitalised, as first run names a place.</summary>
    public static string Greeting(string shown)
    {
        var first = shown.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (!first.Contains('.')) return first;
        var label = first.TrimStart('@').Split('.')[0];
        return label.Length == 0 ? first : char.ToUpperInvariant(label[0]) + label[1..];
    }

    /// <summary>A form sent back to the door once its write is done.</summary>
    private static Form Back(Form form) => form.Given(PostWrites.ReturnField, "/");

    private static Form.Field Given(string name, string holds, string value) => new(name, holds, value, Form.Input.Hidden);

    private static Form.Field Switch(string name, string holds, string? value, IReadOnlyList<Form.Choice> choices)
        => new(name, holds, value, Form.Input.Switch) { Choices = choices };
}

/// <summary>Closes the place's first steps for good, for an actor who holds <see cref="Bit.ManageSpace"/>; closed ones stay
/// closed and nothing else changes.</summary>
public sealed record CloseFirstSteps(string ActorId) : ICommand<TangentSpace>
{
    public async Task<Outcome<TangentSpace>> Run(CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return new Outcome<TangentSpace>.Refused(ChangeTangentSpace.NoPlace);
        var capabilities = await Capabilities.Load(ActorId, null, null, ct);
        if (!capabilities.Exists) return new Outcome<TangentSpace>.Refused(ChangeTangentSpace.NoPlace);
        if (capabilities.Gate(Bit.ManageSpace) is { } gate) return new Outcome<TangentSpace>.Refused(gate);
        if (space.FirstStepsClosed) return new Outcome<TangentSpace>.Done(space);
        space.FirstStepsClosed = true;
        await space.Save(ct);
        return new Outcome<TangentSpace>.Done(space);
    }
}

/// <summary><c>POST /first-steps/close</c> sends <see cref="CloseFirstSteps"/>: a form is sent on to the door, and a JSON
/// request reads where the door is.</summary>
public sealed class FirstStepsController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string ClosePath = "/first-steps/close";

    /// <summary>What closing answers as JSON: the door, where the steps no longer stand.</summary>
    public sealed record Closed(string Door);

    [HttpPost(ClosePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "close_first_steps", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Close the first steps on the door for good, for whoever holds ManageSpace. Whatever they set, the settings change later.")]
    [Needs(Bit.ManageSpace)]
    public async Task<IActionResult> Close(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var outcome = await new CloseFirstSteps(Here.Of(HttpContext).PersonId ?? "").Send();
        if (outcome is Outcome<TangentSpace>.Refused { Reason: var reason }) return this.Refused(reason);
        if (outcome is not Outcome<TangentSpace>.Done) return Answers.NotDone(HttpContext, outcome);
        var door = baseUrl.Resolve(HttpContext) + "/";
        if (PostWrites.WantsJson(Request)) return Ok(new Closed(door));
        Response.Headers.Location = door;
        return StatusCode(StatusCodes.Status303SeeOther);
    }
}
