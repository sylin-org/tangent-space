using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Koan.Web.Attributes;
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
/// listed, answered <c>401</c> to sign in and <c>403</c> otherwise. Its <c>POST</c> takes <c>role</c>, a role's word, and
/// <c>access</c>, one of <see cref="Accesses"/>, as a form or a JSON object, and sends <see cref="ChangeAccess"/>: a form
/// is sent back to the access, and a JSON request reads the role as the page now lists it; a refusal reads the page again
/// with the reason above the list, or a problem. Both resolve the address as the Tangent page does
/// (<see cref="PostWrites.Locate"/>) before anything else is judged, so a Tangent the reader may not view and one no row
/// has answer alike.</summary>
public sealed class TangentAccessController(KoanWebBaseUrl baseUrl, RoleCollection roles) : ControllerBase
{
    public const string Segment = "access";

    public const string Route = TangentSettingsController.Route + "/" + Segment;

    public const string RoleField = "role";

    public const string AccessField = "access";

    public const string AccessRule = "A role’s access to a Tangent is default, read or post.";

    /// <summary>The accesses a role may have, as words.</summary>
    public static readonly IReadOnlyList<string> Accesses = Enum.GetValues<RoleAccess>().Select(Word).ToList();

    public static string Word(RoleAccess access) => access.ToString().ToLowerInvariant();

    [HttpGet(Route)]
    [Tool(Name = "read_tangent_access")]
    [Description("Read which roles read and post in a Tangent, a page at a time, for whoever may change it. Follow with change_tangent_access.")]
    public async Task<IActionResult> Get([Description(Holds.Tangent)] string tangent, [FromQuery(Name = RolesController.PageQuery), Description(Holds.Number)] string? page, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, tangent, null, null, ct);
        if (found is null) return unseen!;
        var read = await Page(found, personId, RolesController.Numbered(page), ct);
        return read.Gate is { } gate ? new ObjectResult(read) { StatusCode = PostWrites.Status(gate) } : Ok(read);
    }

    [HttpPost(Route)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "change_tangent_access", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Set what one role may do in a Tangent, for whoever may change the Tangent and manages the role. Everyone in the role reads or posts there as you set it, at once.")]
    public async Task<IActionResult> Change([FromRoute, Description(Holds.Tangent)] string tangent, ChangeAccessInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, tangent, null, null, ct);
        if (found is null) return unseen!;

        if (TangentWrites.Choice(input.Access, Accesses, out _) is not { } chosen) return await Refused(found, personId, AccessRule, ct);
        if (Roles.KeyOf(input.Role) is not { } key) return await Refused(found, personId, RoleSettings.NoRole, ct);
        var outcome = await new ChangeAccess(found.Tangent.Id, personId ?? "", key, Enum.GetValues<RoleAccess>()[chosen]).Send();
        switch (outcome)
        {
            case Outcome<Tangent>.Done:
                var address = PostWrites.TangentAddress(BaseAddress(), found.Tangent.Id) + "/" + TangentWrites.SettingsSegment + "/" + Segment;
                if (!PostWrites.WantsJson(Request))
                {
                    Response.Headers.Location = address;
                    return StatusCode(StatusCodes.Status303SeeOther);
                }

                var (again, gone) = await PostWrites.Locate(HttpContext, personId, found.Tangent.Id, null, null, ct);
                if (again is null) return gone!;
                var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
                var place = await Capabilities.Load(personId, null, null, ct);
                var role = await roles.Get(key, ct);
                return role is null
                    ? PostWrites.Absent()
                    : new ObjectResult(await Listed(role, again, place, personId, space, ct)) { ContentTypes = { "application/json" } };
            case Outcome<Tangent>.Refused { Reason: var reason } when reason != ChangeTangent.NoTangent:
                return await Refused(found, personId, reason, ct);
            default:
                return PostWrites.Answer(HttpContext, outcome);
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the page again, read afresh,
    /// with the reason above the list.</summary>
    private async Task<IActionResult> Refused(PostWrites.Located found, string? personId, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return PostWrites.Refused(HttpContext, reason);

        var (again, unseen) = await PostWrites.Locate(HttpContext, personId, found.Tangent.Id, null, null, ct);
        if (again is null) return unseen!;
        var page = await Page(again, personId, 1, ct);
        page = reason.Word is { } gate ? page with { Gate = gate, Change = null, Roles = [], Next = null, Previous = null } : page with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = PostWrites.Status(reason) };
    }

    private async Task<TangentAccessPage> Page(PostWrites.Located found, string? personId, int number, CancellationToken ct)
    {
        var row = found.Tangent;
        var baseAddress = BaseAddress();
        var address = PostWrites.TangentAddress(baseAddress, row.Id);
        var settings = address + "/" + TangentWrites.SettingsSegment;
        var here = baseUrl.PathBase + "/t/" + row.Id + "/" + TangentWrites.SettingsSegment + "/" + Segment;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        var place = await Capabilities.Load(personId, null, null, ct);
        var gate = TangentWrites.ManageGate(found.Capabilities)?.Word;
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
    private static async Task<TangentAccessPage.Listed> Listed(Role role, PostWrites.Located found, Capabilities place, string? personId,
        TangentSpace space, CancellationToken ct)
    {
        var access = Overwrites.AccessOf(await Overwrites.Of(found.Tangent.Id, role.Key, ct));
        var offered = TangentWrites.MayManage(found.Capabilities) && space.ModeratorsTangentId != found.Tangent.Id
                      && People.Manages(place, personId, space, role)
            ? Enum.GetValues<RoleAccess>().Where(other => other != access).Select(Word).ToList()
            : [];
        return new TangentAccessPage.Listed(Roles.Word(role.Key), role.Name, Word(access), offered);
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
