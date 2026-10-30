using Koan.Identity.Roles;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Tangents;

/// <summary>A Tangent's access: <c>/t/{tangent}/settings/access</c>, which roles read and post there beyond its
/// audience. Its <c>GET</c> is one page of the roles in Koan's order with what each holds there, one resource both ways
/// (<see cref="TangentAccessPage"/>), by <c>page</c>; a reader who may not manage the Tangent reads the gate with no role
/// listed, answered <c>401</c> to sign in and <c>403</c> otherwise. Its <c>POST</c> takes <c>role</c>, a role's word, and
/// <c>access</c>, one of <see cref="Accesses"/>, as a form or a JSON object, and sends <see cref="ChangeAccess"/>: a form
/// is sent back to the access, and a JSON request reads the role as the page now lists it; a refusal reads the page again
/// with the reason above the list, or a problem. Both resolve the address as the Tangent page does
/// (<see cref="PostWrites.Locate"/>) before anything else is judged, so a Tangent the reader may not view and one no row
/// has answer alike.</summary>
public sealed class TangentAccessController(KoanWebBaseUrl baseUrl, WriteLoop writer, RoleCollection roles) : ControllerBase
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
    public async Task<IActionResult> Get(string tangent, [FromQuery(Name = RolesController.PageQuery)] int? page, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var (found, unseen) = await PostWrites.Locate(personId, tangent, null, null, ct);
        if (found is null) return unseen!;
        var read = await Page(found, personId, Math.Max(page ?? 1, 1), ct);
        return read.Gate is { } gate ? new ObjectResult(read) { StatusCode = PostWrites.Status(gate) } : Ok(read);
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

        if (TangentWrites.Choice(fields, AccessField, Accesses, out _) is not { } chosen) return await Refused(found, personId, AccessRule, ct);
        if (Roles.KeyOf(fields.GetValueOrDefault(RoleField)) is not { } key) return await Refused(found, personId, RoleSettings.NoRole, ct);
        var outcome = await writer.Send(new ChangeAccess(found.Tangent.Id, personId ?? "", key, Enum.GetValues<RoleAccess>()[chosen]), ct);
        switch (outcome)
        {
            case Outcome<Tangent>.Done:
                var address = PostWrites.TangentAddress(BaseAddress(), found.Tangent.Id) + "/" + TangentWrites.SettingsSegment + "/" + Segment;
                if (!PostWrites.WantsJson(Request))
                {
                    Response.Headers.Location = address;
                    return StatusCode(StatusCodes.Status303SeeOther);
                }

                var (again, gone) = await PostWrites.Locate(personId, found.Tangent.Id, null, null, ct);
                if (again is null) return gone!;
                var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
                var place = await Capabilities.Load(personId, null, null, ct);
                var role = await roles.Get(key, ct);
                return role is null
                    ? PostWrites.Absent()
                    : new ObjectResult(await Listed(role, again, place, personId, space, ct)) { ContentTypes = { "application/json" } };
            case Outcome<Tangent>.Refused { Reason: ChangeTangent.NoTangent }:
                return PostWrites.Absent();
            case Outcome<Tangent>.Refused { Reason: var reason }:
                return await Refused(found, personId, reason, ct);
            case Outcome<Tangent>.Failed { Cause: var cause }:
                throw new InvalidOperationException($"Changing the access to the Tangent {tangent} failed.", cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for changing the access to the Tangent {tangent}.");
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the page again, read afresh,
    /// with the reason above the list.</summary>
    private async Task<IActionResult> Refused(PostWrites.Located found, string? personId, string reason, CancellationToken ct)
    {
        var status = Status(reason);
        var gate = reason != RoleSettings.NotManaged && status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden;
        if (PostWrites.WantsJson(Request))
        {
            if (gate) return PostWrites.Gated(reason);
            return reason == RoleSettings.NotManaged
                ? new Problem(status, "Not yours to change", reason, reason).ToResult()
                : new Problem(status, "Not changed", reason).ToResult();
        }

        var (again, unseen) = await PostWrites.Locate(personId, found.Tangent.Id, null, null, ct);
        if (again is null) return unseen!;
        var page = await Page(again, personId, 1, ct);
        page = gate ? page with { Gate = reason, Change = null, Roles = [], Next = null, Previous = null } : page with { Failure = reason };
        return new ObjectResult(page) { StatusCode = status };
    }

    /// <summary>The status a refusal answers with: <c>404</c> for a role no one has, <c>403</c> for a role the actor does
    /// not manage, <c>400</c> for what was sent, and a gate's own otherwise.</summary>
    private static int Status(string reason) => reason switch
    {
        RoleSettings.NoRole => StatusCodes.Status404NotFound,
        RoleSettings.NotManaged => StatusCodes.Status403Forbidden,
        AccessRule or ChangeAccess.Administers or ChangeTangent.Fixed => StatusCodes.Status400BadRequest,
        _ => PostWrites.Status(reason)
    };

    private async Task<TangentAccessPage> Page(PostWrites.Located found, string? personId, int number, CancellationToken ct)
    {
        var row = found.Tangent;
        var baseAddress = BaseAddress();
        var address = PostWrites.TangentAddress(baseAddress, row.Id);
        var settings = address + "/" + TangentWrites.SettingsSegment;
        var here = baseUrl.PathBase + "/t/" + row.Id + "/" + TangentWrites.SettingsSegment + "/" + Segment;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        var place = await Capabilities.Load(personId, null, null, ct);
        var gate = TangentWrites.ManageGate(found.Capabilities);
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
                ? Form.Posting(settings + "/" + Segment, FormToken.For(HttpContext),
                    new Form.Field(RoleField, "The word of a role listed here with the access in its offered."),
                    new Form.Field(AccessField, "default, as its role allows in the place; read, reading alone; or post, reading and posting."))
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
