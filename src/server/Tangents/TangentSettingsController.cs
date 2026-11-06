using System.ComponentModel;
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

/// <summary>What changing a Tangent's settings sends, as a form or one JSON object.</summary>
public sealed record ChangeTangentInput(
    [Description(Holds.ChangedTangentName)] string? Name = null,
    [Description(Holds.ChangedTangentDescription)] string? Description = null,
    [Description(Holds.ChangedTangentKind)] string? Kind = null,
    [Description(Holds.ChangedTangentAudience)] string? Audience = null,
    [Description(Holds.TangentMove)] string? Move = null,
    [Description(Holds.TangentAccent)] string? Accent = null,
    [Description(Holds.TangentImage)] string? Image = null);

/// <summary>What deleting a Tangent sends, as a form or one JSON object.</summary>
public sealed record DeleteTangentInput([Description(Holds.DeletedTopics)] int? Topics = null);

/// <summary>A Tangent's settings: <c>/t/{tangent}/settings</c>. Its <c>GET</c> is the form, one resource both ways
/// (<see cref="TangentSettingsPage"/>). Its <c>POST</c> takes <c>name</c>, <c>description</c>, <c>kind</c>,
/// <c>audience</c>, <c>move</c>, <c>accent</c> and <c>image</c>, as a form or a JSON object, each left as it is when not sent, and sends
/// <see cref="ChangeTangent"/>: a form is sent on to the Tangent's page, or back to its settings after a move, or, where
/// the change closed the Tangent to its sender, reads that it is saved (<see cref="TangentClosed"/>), as a JSON request
/// does; a JSON
/// request reads the Tangent (<c>200</c>); a refusal reads the form again with the reason inline, or a problem with the
/// gate. Both resolve the address as the Tangent page does (<see cref="Here.Find"/>) before anything else is
/// judged, so a Tangent the reader may not view and one no row has answer alike. <c>/t/{tangent}/settings/image</c> takes
/// <c>file</c>, an image of the sender's own as the file of a multipart form, and wears it as the Tangent's image
/// (<see cref="TakeImage"/>), which an agent does as <c>bring_in</c> then <c>change_tangent</c>. <c>/t/{tangent}/settings/delete</c>
/// takes <c>topics</c>, as a form or a JSON object, and sends <see cref="DeleteTangent"/>: a form is sent on to where its
/// <c>return</c> names, or to the Tangents (<see cref="TangentsController"/>), where it is gone, and a JSON request reads
/// the Tangent as it stood (<c>200</c>); a refusal reads the settings again with the reason in the delete's warning,
/// opened, or a problem.</summary>
[Managing]
public sealed class TangentSettingsController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Route = "/t/{tangent}/" + TangentWrites.SettingsSegment;

    public const string DeleteRoute = Route + "/" + TangentWrites.DeleteSegment;

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
        var personId = PostWrites.PersonId(User);
        var found = Here.Of(HttpContext);
        return Ok(await Page(found, personId, ct));
    }

    [HttpPost(Route)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "change_tangent", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Change a Tangent's name, description, default kind, audience, accent or image, or move it among the Tangents, for whoever holds ManageTangent there or ManageSpace. What you send replaces what stood, and sending a move again moves it again.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> Change([FromRoute, Description(Holds.Tangent)] string tangent, ChangeTangentInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var found = Here.Of(HttpContext);

        var kind = TangentWrites.Choice(input.Kind, TangentWrites.Kinds, out var kindValid);
        if (!kindValid) return await Refused(found, personId, input, TangentWrites.KindRule, ct);
        var audience = TangentWrites.Choice(input.Audience, TangentWrites.Audiences, out var audienceValid);
        if (!audienceValid) return await Refused(found, personId, input, TangentWrites.AudienceRule, ct);
        var move = TangentWrites.Choice(input.Move, TangentWrites.Moves, out var moveValid);
        if (!moveValid) return await Refused(found, personId, input, TangentWrites.MoveRule, ct);

        var outcome = await new ChangeTangent(
            found.Tangent!.Id,
            personId ?? "",
            input.Name,
            input.Description,
            kind is { } chosenKind ? Enum.GetValues<PostKind>()[chosenKind] : null,
            audience is { } chosenAudience ? Enum.GetValues<Audience>()[chosenAudience] : null,
            move is { } chosenMove ? Enum.GetValues<Move>()[chosenMove] : null,
            input.Accent,
            input.Image).Send();
        switch (outcome)
        {
            case Outcome<Tangent>.Done { Result: var changed }:
                if (!(await Capabilities.Load(personId, changed.Id, null, ct)).Exists) return await Closed(changed, personId, ct);
                var address = PostWrites.TangentAddress(BaseAddress(), changed.Id);
                return await TangentWrites.Written(HttpContext, changed, BaseAddress(), personId, created: false,
                    move is null ? address : address + "/" + TangentWrites.SettingsSegment, ct);
            case Outcome<Tangent>.Refused { Reason: { Kind: not ReasonKind.Absent } reason }:
                return await Refused(found, personId, input, reason, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>An image of the reader's own, sent as the file of a multipart form, kept as every image is
    /// (<see cref="Media.KeepFile"/>) and worn at once as the Tangent's image, for whoever may change it; the form is sent on
    /// to the settings, or as its <c>return</c> says, and a refusal reads the settings again with the reason above the form.</summary>
    [HttpPost(ImageRoute)]
    [RequestSizeLimit(NewMediaController.RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = NewMediaController.RequestLimit)]
    [Needs(Bit.View)]
    public async Task<IActionResult> TakeImage([FromRoute] string tangent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var found = Here.Of(HttpContext);
        var sent = new ChangeTangentInput();
        if (personId is null || found.Capabilities.Gate(Bit.ManageTangent) is not null)
            return await Refused(found, personId, sent, found.Capabilities.Gate(Bit.ManageTangent) ?? Capabilities.SignIn, ct);

        Media media;
        try
        {
            media = await Media.KeepFile(Request.HasFormContentType ? await Request.ReadFormAsync(ct) : null, NewMediaController.FileField, ct);
        }
        catch (Refusal refusal)
        {
            return await Refused(found, personId, sent, refusal.Reason, ct);
        }

        await new BringMedia(media.Id, personId).Send();
        return await new ChangeTangent(found.Tangent!.Id, personId, Image: Media.Reference(media.Id)).Send() switch
        {
            Outcome<Tangent>.Done { Result: var changed } => await TangentWrites.Written(HttpContext, changed, BaseAddress(), personId, created: false,
                PostWrites.TangentAddress(BaseAddress(), changed.Id) + "/" + TangentWrites.SettingsSegment, ct),
            Outcome<Tangent>.Refused { Reason: { Kind: not ReasonKind.Absent } reason } => await Refused(found, personId, sent, reason, ct),
            var outcome => Answers.NotDone(HttpContext, outcome)
        };
    }

    [HttpPost(DeleteRoute)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "delete_tangent", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Delete a Tangent and everything in it, for whoever holds ManageSpace: its Topics and their replies go for good and no tombstone stays, and copies of its articles on Bluesky are taken down wherever their authors’ accounts still let this place. An empty Tangent goes as it is; one that holds Topics goes only when topics says how many, as read_tangent_settings counts them, and /settings/export/{tangent}.zip keeps a copy first. The moderators’ Tangent stays.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> Delete([FromRoute, Description(Holds.Tangent)] string tangent, DeleteTangentInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var found = Here.Of(HttpContext);
        var outcome = await new DeleteTangent(found.Tangent!.Id, personId ?? "", input.Topics ?? 0).Send();
        switch (outcome)
        {
            case Outcome<Tangent>.Done { Result: var deleted }:
                var tangents = BaseAddress() + TangentsController.Route;
                if (PostWrites.WantsJson(Request)) return Ok(new DeletedTangent(deleted.Id, deleted.Name, deleted.TopicCount, tangents));
                Response.Headers.Location = PostWrites.Onward(HttpContext, BaseAddress(), tangents);
                return StatusCode(StatusCodes.Status303SeeOther);
            case Outcome<Tangent>.Refused { Reason: { Kind: not ReasonKind.Absent and not ReasonKind.Gated } reason } when !PostWrites.WantsJson(Request):
                var (again, gone) = await Here.Find(personId, found.Tangent!.Id, null, null, ct);
                if (gone is not null) return this.Refused(gone);
                var page = await Page(again, personId, ct);
                return page.Delete is null
                    ? this.Refused(reason)
                    : new ObjectResult(page with { DeleteFailure = reason.Text }) { StatusCode = Answers.Status(HttpContext, reason) };
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>What a change answers that closed the Tangent to its own sender, as a page or as JSON: it is saved, and
    /// absent to them now, so it says so in a sentence and leads to the door, and says nothing else of the Tangent.</summary>
    private async Task<IActionResult> Closed(Tangent tangent, string? personId, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        var place = await Capabilities.Load(personId, null, null, ct);
        return Ok(new TangentClosed(tangent.Name, space.Name, baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, baseUrl.PathBase + "/", ct)));
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again, read afresh,
    /// keeping what was sent, with the reason where it belongs.</summary>
    private async Task<IActionResult> Refused(Here found, string? personId, ChangeTangentInput input, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);
        var (again, gone) = await Here.Find(personId, found.Tangent!.Id, null, null, ct);
        if (gone is not null) return this.Refused(gone);
        var page = await Page(again, personId, ct);
        page = page with
        {
            Name = input.Name ?? page.Name,
            Description = input.Description ?? page.Description,
            Accent = input.Accent is { } accent && (accent.Length == 0 || Render.Tile.Tones.Contains(accent)) ? accent : page.Accent,
            Image = input.Image is { } image && Space.Art.Judged(image) is { } kept ? kept : page.Image,
            Kind = TangentWrites.Choice(input.Kind, TangentWrites.Kinds, out _) is { } kind ? TangentWrites.Kinds[kind] : page.Kind,
            Audience = TangentWrites.Choice(input.Audience, TangentWrites.Audiences, out _) is { } audience
                ? TangentWrites.Audiences[audience]
                : page.Audience
        };
        page = TangentWrites.Concerning(reason) switch
        {
            TangentWrites.About.Name => page with { NameFailure = reason.Text },
            TangentWrites.About.Description => page with { DescriptionFailure = reason.Text },
            TangentWrites.About.Sent => page with { Failure = reason.Text },
            _ => page with { Gate = reason.Word, Change = null }
        };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    private async Task<TangentSettingsPage> Page(Here found, string? personId, CancellationToken ct)
    {
        var row = found.Tangent!;
        var capabilities = found.Capabilities;
        var baseAddress = BaseAddress();
        var address = PostWrites.TangentAddress(baseAddress, row.Id);
        var here = baseUrl.PathBase + "/t/" + row.Id + "/" + TangentWrites.SettingsSegment;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
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
            capabilities.Holds(Bit.ManageTangent)
                ? TangentWrites.ChangeForm(address + "/" + TangentWrites.SettingsSegment, moderators, movable)
                : null,
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

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
