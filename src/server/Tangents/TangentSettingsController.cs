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
    [Description(Holds.TangentMove)] string? Move = null);

/// <summary>A Tangent's settings: <c>/t/{tangent}/settings</c>. Its <c>GET</c> is the form, one resource both ways
/// (<see cref="TangentSettingsPage"/>). Its <c>POST</c> takes <c>name</c>, <c>description</c>, <c>kind</c>,
/// <c>audience</c> and <c>move</c>, as a form or a JSON object, each left as it is when not sent, and sends
/// <see cref="ChangeTangent"/>: a form is sent on to the Tangent's page, or back to its settings after a move, or, where
/// the change closed the Tangent to its sender, reads that it is saved (<see cref="TangentClosed"/>), as a JSON request
/// does; a JSON
/// request reads the Tangent (<c>200</c>); a refusal reads the form again with the reason inline, or a problem with the
/// gate. Both resolve the address as the Tangent page does (<see cref="Here.Find"/>) before anything else is
/// judged, so a Tangent the reader may not view and one no row has answer alike.</summary>
public sealed class TangentSettingsController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Route = "/t/{tangent}/" + TangentWrites.SettingsSegment;

    [HttpGet(Route)]
    [Tool(Name = "read_tangent_settings")]
    [Description("Read a Tangent's name, description, default kind and audience, for whoever may change them. Call this before change_tangent.")]
    public async Task<IActionResult> Get([Description(Holds.Tangent)] string tangent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var (found, refused) = await Here.Find(personId, tangent, null, null, ct);
        if (refused is not null) return this.Refused(refused);
        return Ok(await Page(found, personId, ct));
    }

    [HttpPost(Route)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "change_tangent", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Change a Tangent's name, description, default kind or audience, or move it among the Tangents, for whoever holds ManageTangent there or ManageSpace. What you send replaces what stood, and sending a move again moves it again.")]
    public async Task<IActionResult> Change([FromRoute, Description(Holds.Tangent)] string tangent, ChangeTangentInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var (found, refused) = await Here.Find(personId, tangent, null, null, ct);
        if (refused is not null) return this.Refused(refused);

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
            move is { } chosenMove ? Enum.GetValues<Move>()[chosenMove] : null).Send();
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
        return new TangentSettingsPage(
            row.Id,
            row.Name,
            row.Description,
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
            await PostWrites.Reader(personId, capabilities, baseAddress, here, ct));
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
