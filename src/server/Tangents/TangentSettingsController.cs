using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Sorting;
using Koan.Identity.Roles;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Tangents;

/// <summary>A Tangent's settings: <c>/t/{tangent}/settings</c>. Its <c>GET</c> is the form, one resource both ways
/// (<see cref="TangentSettingsPage"/>). Its <c>POST</c> takes <c>name</c>, <c>description</c>, <c>kind</c>,
/// <c>audience</c> and <c>move</c>, as a form or a JSON object, each left as it is when not sent, and sends
/// <see cref="ChangeTangent"/>: a form is sent on to the Tangent's page, or back to its settings after a move; a JSON
/// request reads the Tangent (<c>200</c>); a refusal reads the form again with the reason inline, or a problem with the
/// gate. Both resolve the address as the Tangent page does (<see cref="PostWrites.Locate"/>) before anything else is
/// judged, so a Tangent the reader may not view and one no row has answer alike.</summary>
public sealed class TangentSettingsController(KoanWebBaseUrl baseUrl, WriteLoop writer, RoleCollection roles) : ControllerBase
{
    public const string Route = "/t/{tangent}/" + TangentWrites.SettingsSegment;

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
    public async Task<IActionResult> Change(string tangent, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        var (found, unseen) = await PostWrites.Locate(personId, tangent, null, null, ct);
        if (found is null) return unseen!;
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();

        var kind = TangentWrites.Choice(fields, TangentWrites.KindField, TangentWrites.Kinds, out var kindValid);
        if (!kindValid) return await Refused(found, personId, fields, TangentWrites.KindRule, ct);
        var audience = TangentWrites.Choice(fields, TangentWrites.AudienceField, TangentWrites.Audiences, out var audienceValid);
        if (!audienceValid) return await Refused(found, personId, fields, TangentWrites.AudienceRule, ct);
        var move = TangentWrites.Choice(fields, TangentWrites.MoveField, TangentWrites.Moves, out var moveValid);
        if (!moveValid) return await Refused(found, personId, fields, TangentWrites.MoveRule, ct);

        var outcome = await writer.Send(new ChangeTangent(
            found.Tangent.Id,
            personId ?? "",
            fields.GetValueOrDefault(TangentWrites.NameField),
            fields.GetValueOrDefault(TangentWrites.DescriptionField),
            kind is { } chosenKind ? Enum.GetValues<PostKind>()[chosenKind] : null,
            audience is { } chosenAudience ? Enum.GetValues<Audience>()[chosenAudience] : null,
            move is { } chosenMove ? Enum.GetValues<Move>()[chosenMove] : null), ct);
        switch (outcome)
        {
            case Outcome<Tangent>.Done { Result: var changed }:
                var address = PostWrites.TangentAddress(Configured(), changed.Id);
                return await TangentWrites.Written(HttpContext, changed, Configured(), personId, created: false,
                    move is null ? address : address + "/" + TangentWrites.SettingsSegment, ct);
            case Outcome<Tangent>.Refused { Reason: ChangeTangent.NoTangent }:
                return PostWrites.Absent();
            case Outcome<Tangent>.Refused { Reason: var reason }:
                return await Refused(found, personId, fields, reason, ct);
            case Outcome<Tangent>.Failed { Cause: var cause }:
                throw new InvalidOperationException($"Changing the Tangent {tangent} failed.", cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for changing the Tangent {tangent}.");
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again, read afresh,
    /// keeping what was sent, with the reason where it belongs.</summary>
    private async Task<IActionResult> Refused(PostWrites.Located found, string? personId, IReadOnlyDictionary<string, string> fields, string reason,
        CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return TangentWrites.Refused(reason);
        var (again, unseen) = await PostWrites.Locate(personId, found.Tangent.Id, null, null, ct);
        if (again is null) return unseen!;
        var page = await Page(again, personId, ct);
        page = page with
        {
            Name = fields.GetValueOrDefault(TangentWrites.NameField) ?? page.Name,
            Description = fields.GetValueOrDefault(TangentWrites.DescriptionField) ?? page.Description,
            Kind = TangentWrites.Choice(fields, TangentWrites.KindField, TangentWrites.Kinds, out _) is { } kind ? TangentWrites.Kinds[kind] : page.Kind,
            Audience = TangentWrites.Choice(fields, TangentWrites.AudienceField, TangentWrites.Audiences, out _) is { } audience
                ? TangentWrites.Audiences[audience]
                : page.Audience
        };
        page = TangentWrites.Concerning(reason) switch
        {
            TangentWrites.About.Name => page with { NameFailure = reason },
            TangentWrites.About.Description => page with { DescriptionFailure = reason },
            TangentWrites.About.Sent => page with { Failure = reason },
            _ => page with { Gate = reason }
        };
        return new ObjectResult(page) { StatusCode = TangentWrites.Status(reason) };
    }

    private async Task<TangentSettingsPage> Page(PostWrites.Located found, string? personId, CancellationToken ct)
    {
        var row = found.Tangent;
        var capabilities = found.Capabilities;
        var configured = Configured();
        var address = PostWrites.TangentAddress(configured, row.Id);
        var here = baseUrl.PathBase + "/t/" + row.Id + "/" + TangentWrites.SettingsSegment;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        var moderators = space.ModeratorsTangentId == row.Id;
        return new TangentSettingsPage(
            row.Id,
            row.Name,
            row.Description,
            PostWrites.Word(row.DefaultKind),
            TangentWrites.Word(await Overwrites.AudienceOf(roles, row.Id, ct) ?? Audience.Public),
            moderators ? null : (await Neighbour(row, earlier: true, ct))?.Name,
            moderators ? null : (await Neighbour(row, earlier: false, ct))?.Name,
            moderators,
            address,
            address + "/" + TangentWrites.SettingsSegment,
            FormToken.For(HttpContext),
            capabilities.Can,
            TangentWrites.ManageGate(capabilities),
            TangentWrites.Kinds,
            moderators ? [] : TangentWrites.Audiences,
            personId is null ? configured + Participants.SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            configured + "/",
            await PostWrites.Reader(personId, capabilities, configured, here, ct));
    }

    /// <summary>The Tangent just before or just after <paramref name="tangent"/> in the Tangents' order, the moderators'
    /// Tangent, which comes after every other, aside; null at either end. One bounded read of one row.</summary>
    private static async Task<Tangent?> Neighbour(Tangent tangent, bool earlier, CancellationToken ct)
    {
        var order = FieldPath.Of(nameof(Tangent.Order));
        var id = FieldPath.Of(nameof(Tangent.Id));
        var beyond = earlier ? FilterOperator.Lt : FilterOperator.Gt;
        var query = new QueryDefinition
        {
            Filter = Filter.Any(
                Filter.On(order, beyond, FilterValue.Of(tangent.Order)),
                Filter.All(Filter.On(order, FilterOperator.Eq, FilterValue.Of(tangent.Order)), Filter.On(id, beyond, FilterValue.Of(tangent.Id)))),
            Page = 1,
            PageSize = 1
        }.WithSort<Tangent>(sort =>
        {
            if (earlier) sort.OrderByDescending(row => row.Order).ThenByDescending(row => row.Id);
            else sort.OrderBy(row => row.Order).ThenBy(row => row.Id);
        });
        var rows = await Tangent.Query(row => row.Order < ModeratorsTangent.Order, query, ct);
        return rows.Count == 0 ? null : rows[0];
    }

    private string Configured() => baseUrl.Configured
        ?? throw new InvalidOperationException("Koan:Web:ApplicationUrl must be set to the base URL the Tangent Space answers at.");
}
