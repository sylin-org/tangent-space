using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>What setting a role's access to a Tangent sends, as a form or one JSON object.</summary>
public sealed record ChangeAccessInput(
    [Description(Holds.AccessRole)] string? Role = null,
    [Description(Holds.Access)] string? Access = null);

/// <summary>A Tangent's access: <c>/t/{tangent}/settings/access</c>, which roles read and post there beyond its
/// audience. Its <c>GET</c> is one page of the roles in Koan's order with what each holds there, one resource both ways
/// (<see cref="TangentAccessPage"/>), by <c>page</c>; a reader who may not manage the Tangent reads the gate with no role
/// listed, answered <c>401</c> to sign in and <c>403</c> otherwise, and a JSON reader the problem with its word. Its <c>POST</c> takes <c>role</c>, a role's word, where
/// <c>everyone</c> is refused since the Tangent's audience says what everyone reads (<see cref="ChangeAccess.EveryoneReads"/>), and <c>access</c>, one of <see cref="Accesses"/>, as a form or a JSON object, and sends <see cref="ChangeAccess"/>: a form
/// is sent back to the access, and a JSON request reads the role as the page now lists it; a refusal reads the page again
/// with the reason above the list, or a problem. Both resolve the address as the Tangent page does
/// (<see cref="Here.Find"/>) before anything else is judged, so a Tangent the reader may not view and one no row
/// has answer alike.</summary>
[Managing]
public sealed class TangentAccessController(KoanWebBaseUrl baseUrl, RoleCollection roles) : ControllerBase
{
    public const string Segment = "access";

    public const string Route = TangentSettingsController.Route + "/" + Segment;

    public const string RoleField = "role";

    public const string AccessField = "access";

    public static readonly Reason AccessRule = Reason.Invalid("A role’s access to a Tangent is default, read or post.");

    /// <summary>The accesses a role may have, as words.</summary>
    public static readonly IReadOnlyList<string> Accesses = Enum.GetValues<RoleAccess>().Select(Word).ToList();

    public static string Word(RoleAccess access) => access.ToString().ToLowerInvariant();

    [HttpGet(Route)]
    [Tool(Name = "read_tangent_access")]
    [Description("Read which roles read and post in a Tangent, a page at a time, for whoever may change it. Then call change_tangent_access.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> Get([Description(Holds.Tangent)] string tangent, [FromQuery(Name = RolesController.PageQuery), Description(Holds.Number)] string? page, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var found = Here.Of(HttpContext);
        // Judged here, not declared: a person who may not change the Tangent, sent here from its settings, reads the page
        // with the gate in place, where a declaration answers a refusal; JSON reads the same refusal.
        var gate = found.Capabilities.Gate(Bit.ManageTangent);
        if (gate is not null && PostWrites.WantsJson(Request)) return this.Refused(gate);
        var read = await Page(found, personId, RolesController.Numbered(page), ct);
        return gate is not null ? new ObjectResult(read) { StatusCode = Answers.Status(HttpContext, gate) } : Ok(read);
    }

    [HttpPost(Route)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "change_tangent_access", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Set what one role may do in a Tangent, for whoever may change the Tangent and manages the role. Everyone in the role reads or posts there as you set it, at once.")]
    [Needs(Bit.ManageTangent)]
    public async Task<IActionResult> Change([FromRoute, Description(Holds.Tangent)] string tangent, ChangeAccessInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var found = Here.Of(HttpContext);

        if (TangentWrites.Choice(input.Access, Accesses, out _) is not { } chosen) return await Refused(found, personId, AccessRule, ct);
        if ((input.Role == Roles.Word(Roles.Everyone) ? Roles.Everyone : Roles.KeyOf(input.Role)) is not { } key)
            return await Refused(found, personId, RoleSettings.NoRole, ct);
        var outcome = await new ChangeAccess(found.Tangent!.Id, personId ?? "", key, Enum.GetValues<RoleAccess>()[chosen]).Send();
        switch (outcome)
        {
            case Outcome<Tangent>.Done:
                var address = PostWrites.TangentAddress(BaseAddress(), found.Tangent!.Id) + "/" + TangentWrites.SettingsSegment + "/" + Segment;
                if (!PostWrites.WantsJson(Request))
                {
                    Response.Headers.Location = address;
                    return StatusCode(StatusCodes.Status303SeeOther);
                }

                var (again, gone) = await Here.Find(personId, found.Tangent!.Id, null, null, ct);
                if (gone is not null) return this.Refused(gone);
                var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
                var place = await Capabilities.Load(personId, null, null, ct);
                var role = await roles.Get(key, ct);
                return role is null
                    ? this.Refused(Capabilities.Absent)
                    : new ObjectResult(await Listed(role, again, place, personId, space, ct)) { ContentTypes = { "application/json" } };
            case Outcome<Tangent>.Refused { Reason: var reason } when reason != ChangeTangent.NoTangent:
                return await Refused(found, personId, reason, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the page again, read afresh,
    /// with the reason above the list.</summary>
    private async Task<IActionResult> Refused(Here found, string? personId, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);

        var (again, gone) = await Here.Find(personId, found.Tangent!.Id, null, null, ct);
        if (gone is not null) return this.Refused(gone);
        var page = await Page(again, personId, 1, ct);
        if (page.Gate is null) page = page with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    private async Task<TangentAccessPage> Page(Here found, string? personId, int number, CancellationToken ct)
    {
        var row = found.Tangent!;
        var baseAddress = BaseAddress();
        var address = PostWrites.TangentAddress(baseAddress, row.Id);
        var settings = address + "/" + TangentWrites.SettingsSegment;
        var here = baseUrl.PathBase + "/t/" + row.Id + "/" + TangentWrites.SettingsSegment + "/" + Segment;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        var place = await Capabilities.Load(personId, null, null, ct);
        var gate = found.Capabilities.Gate(Bit.ManageTangent)?.Word;
        var read = gate is null ? await roles.Page(page: number, pageSize: RolesController.PageSize, ct: ct) : null;
        var listed = new List<TangentAccessPage.Listed>();
        foreach (var role in read?.Items.Where(Listable) ?? []) listed.Add(await Listed(role, found, place, personId, space, ct));
        return new TangentAccessPage(
            row.Id,
            row.Name,
            address,
            settings,
            TangentWrites.Word(await Overwrites.AudienceOf(row.Id, ct) ?? Audience.Public),
            space.ModeratorsTangentId == row.Id,
            listed,
            read is not null && (long)number * RolesController.PageSize < read.TotalCount ? settings + "/" + Segment + "?" + RolesController.PageQuery + "=" + (number + 1) : null,
            read is not null && number > 1 ? settings + "/" + Segment + (number > 2 ? "?" + RolesController.PageQuery + "=" + (number - 1) : "") : null,
            gate is null && space.ModeratorsTangentId != row.Id
                ? Form.Posting(settings + "/" + Segment,
                    new Form.Field(RoleField, Holds.AccessRole),
                    new Form.Field(AccessField, Holds.Access))
                : null,
            found.Capabilities.Can,
            gate,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, found.Capabilities, baseAddress, here, ct));
    }

    /// <summary>Whether a role's access is set here: not everyone's, which is the audience, and not one that administers,
    /// which reads and posts everywhere.</summary>
    private static bool Listable(Role role)
        => role.Key != Roles.Everyone && !role.Permissions.Contains(RoleTokens.Administrator, StringComparer.Ordinal);

    /// <summary>One role as the access lists it for the reader, from one keyed read of its overwrite here: what it holds,
    /// and, where the reader may manage the Tangent and manages the role, the access they may set it to instead.</summary>
    private static async Task<TangentAccessPage.Listed> Listed(Role role, Here found, Capabilities place, string? personId,
        TangentSpace space, CancellationToken ct)
    {
        var access = Overwrites.AccessOf(await Overwrites.Of(found.Tangent!.Id, role.Key, ct));
        var offered = found.Capabilities.Holds(Bit.ManageTangent) && space.ModeratorsTangentId != found.Tangent!.Id
                      && People.Manages(place, personId, space, role)
            ? Enum.GetValues<RoleAccess>().Where(other => other != access).Select(Word).ToList()
            : [];
        return new TangentAccessPage.Listed(Roles.Word(role.Key), role.Name, Word(access), offered);
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
