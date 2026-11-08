using System.ComponentModel;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>A Tangent's settings: <c>/t/{tangent}/settings</c>, the form that changes it and the one that deletes it, one
/// resource both ways (<see cref="TangentSettingsPage"/>); both forms send to the Tangent's own address
/// (<see cref="TangentsController"/>). It resolves the address as the Tangent page does (<see cref="Here.Find"/>) before
/// anything else is judged, so a Tangent the reader may not view and one no row has answer alike.
/// <c>/t/{tangent}/settings/image</c> takes <c>file</c>, an image of the sender's own as the file of a multipart form, and
/// wears it as the Tangent's image (<see cref="TakeImage"/>), which an agent does as <c>bring_in</c> then
/// <c>change_tangent</c>.</summary>
[Managing]
public sealed class TangentSettingsController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Route = "/t/{tangent}/" + TangentWrites.SettingsSegment;

    public const string ImageRoute = Route + "/" + TangentWrites.ImageSegment;

    /// <summary>The most copies on Bluesky the delete's warning counts; past it, it says "at least".</summary>
    public const int CopyLimit = 100;

    [HttpGet(Route)]
    [Tool(Name = "read_tangent_settings")]
    [Description("Read a Tangent's name, description, default kind, audience, accent and image, for whoever may change them, and, for whoever holds ManageSpace, what deleting it takes along. Call this before change_tangent or delete_tangent.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> Get([Description(Holds.Tangent)] string tangent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await Compose(Here.Of(HttpContext), baseUrl.Resolve(HttpContext), baseUrl.PathBase, ct));
    }

    /// <summary>An image of the reader's own, sent as the file of a multipart form, kept as every image is
    /// (<see cref="Media.KeepFile"/>) and worn at once as the Tangent's image, for whoever may change it (<see cref="WearImage"/>);
    /// the form is sent on to the settings, or as its <c>return</c> says, and a refusal reads the settings again with the reason
    /// above the form.</summary>
    [HttpPost(ImageRoute)]
    [RequestSizeLimit(NewMediaController.RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = NewMediaController.RequestLimit)]
    [Needs(Bit.ManageTangent)]
    public async Task<IActionResult> TakeImage([FromRoute] string tangent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var found = Here.Of(HttpContext);
        var personId = found.PersonId!;
        var baseAddress = baseUrl.Resolve(HttpContext);

        Media media;
        try
        {
            media = await Media.KeepFile(Request.HasFormContentType ? await Request.ReadFormAsync(ct) : null, NewMediaController.FileField, ct);
        }
        catch (Refusal refusal)
        {
            return await Refused(found, refusal.Reason, ct);
        }

        await new BringMedia(media.Id, personId).Send();
        return await new WearImage(found.Tangent!.Id, personId, Media.Reference(media.Id)).Send() switch
        {
            Outcome<Tangent>.Done { Result: var changed } => this.Written(TangentsController.Route + "/" + Uri.EscapeDataString(changed.Id) + "/" + TangentWrites.SettingsSegment,
                await WrittenTangent.Of(changed, baseAddress, personId, ct), created: false),
            Outcome<Tangent>.Refused { Reason: { Kind: not ReasonKind.Absent } reason } => await Refused(found, reason, ct),
            var outcome => Answers.NotDone(HttpContext, outcome)
        };
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the settings again with the
    /// reason above the form.</summary>
    private async Task<IActionResult> Refused(Here found, Reason reason, CancellationToken ct)
    {
        if (Answers.WantsJson(Request)) return Answers.Refused(HttpContext, reason);
        var page = await Compose(found, baseUrl.Resolve(HttpContext), baseUrl.PathBase, ct) with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    /// <summary>The settings of the Tangent <paramref name="found"/> names, as its reader finds them under
    /// <paramref name="baseAddress"/>, whose path is <paramref name="pathBase"/>: the form that changes it where they may, and,
    /// where they hold <see cref="Bit.ManageSpace"/>, its place among the Tangents and the form that deletes it, saying what goes
    /// with it.</summary>
    public static async Task<TangentSettingsPage> Compose(Here found, string baseAddress, string pathBase, CancellationToken ct)
    {
        var row = found.Tangent!;
        var personId = found.PersonId;
        var capabilities = found.Capabilities;
        var address = PostWrites.TangentAddress(baseAddress, row.Id);
        var here = pathBase + "/t/" + row.Id + "/" + TangentWrites.SettingsSegment;
        var space = found.Rows.Space;
        var moderators = space.ModeratorsTangentId == row.Id;
        var movable = personId is not null && (await Capabilities.Load(personId, null, null, ct)).Holds(Bit.ManageSpace);
        var neighbours = movable && !moderators;
        var deletable = movable && !moderators;
        var (copies, more) = deletable && row.TopicCount > 0 ? await Connections.Receipt.Out(row.Id, CopyLimit, ct) : (0, false);
        return new TangentSettingsPage(
            row.Id,
            row.Name,
            row.Description,
            row.Accent,
            Render.Tile.For(row, baseAddress + "/").Tone,
            row.Image,
            Space.Art.Covers.Select(cover => new Space.AppearanceSettingsPage.Cover(cover.Name, cover.Title, baseAddress + "/" + Space.Art.File(cover.Name))).ToList(),
            PostWrites.Word(row.DefaultKind),
            TangentWrites.Word(await Overwrites.AudienceOf(row.Id, ct) ?? Audience.Public),
            neighbours ? (await Neighbour(personId, row, earlier: true, ct))?.Name : null,
            neighbours ? (await Neighbour(personId, row, earlier: false, ct))?.Name : null,
            moderators,
            movable,
            address,
            address + "/" + TangentWrites.SettingsSegment + "/" + TangentAccessController.Segment,
            capabilities.Holds(Bit.ManageTangent) ? TangentWrites.ChangeForm(address, moderators, movable) : null,
            capabilities.Can,
            capabilities.Gate(Bit.ManageTangent)?.Word,
            TangentWrites.Kinds,
            moderators ? [] : TangentWrites.Audiences,
            personId is null ? baseAddress + Participants.SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, capabilities, baseAddress, here, ct))
        {
            Delete = deletable ? TangentWrites.DeleteForm(baseAddress, row.Id, row.TopicCount) : null,
            Topics = row.TopicCount,
            OnBluesky = deletable && row.TopicCount > 0 ? copies : null,
            OnBlueskyAtLeast = more,
            Export = deletable && row.TopicCount > 0 ? Lifecycle.ExportController.ZipOf(baseAddress, row.Id) : null
        };
    }

    /// <summary>The nearest Tangent before or after <paramref name="tangent"/> in the Tangents' order that the reader may
    /// view, the moderators' Tangent, which comes after every other, aside; null where none is. The Tangents that way are
    /// read a page at a time (<see cref="Tangent.InOrder"/>), each judged from Koan's cache, until one is found.</summary>
    private static async Task<Tangent?> Neighbour(string? personId, Tangent tangent, bool earlier, CancellationToken ct)
    {
        (int Order, string Id)? from = (tangent.Order, tangent.Id);
        while (true)
        {
            var rows = await Tangent.InOrder(from, earlier, aside: null, ViewableTangents.ReadSize, ct);
            var candidates = rows.Where(row => row.Order < ModeratorsTangent.Order).ToList();
            var each = await Capabilities.LoadEach(personId, candidates.Select(row => row.Id).ToList(), ct);
            if (candidates.FirstOrDefault(row => each[row.Id].Exists) is { } found) return found;
            if (rows.Count < ViewableTangents.ReadSize) return null;
            from = (rows[^1].Order, rows[^1].Id);
        }
    }
}

/// <summary>An image brought in here worn as the Tangent <see cref="Slug"/>'s image by <see cref="ActorId"/>, as
/// <c>change_tangent</c>'s image wears one (<see cref="TangentWriting.Changed"/>): its use moves with it, and the change is
/// logged; the Doorway judged that the actor may change the Tangent.</summary>
public sealed record WearImage(string Slug, string ActorId, string Image) : ICommand<Tangent>
{
    public async Task<Outcome<Tangent>> Run(CancellationToken ct)
    {
        if (await Tangent.Get(Slug, ct) is not { } prior || await Tangent.Get(Slug, ct) is not { } working) return new Outcome<Tangent>.Refused(Tangent.NoTangent);
        working.Image = Image;
        if (await TangentWriting.Changed(prior, working, null, null, ActorId, DateTimeOffset.UtcNow, ct)) await working.Save(ct);
        return new Outcome<Tangent>.Done(working);
    }
}
