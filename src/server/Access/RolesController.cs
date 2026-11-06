using System.ComponentModel;
using System.Globalization;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Access;

/// <summary>What creating a role sends, as a form or one JSON object: <see cref="Permissions"/> as a form's checkboxes
/// send them, one to a value, or in one value separated by spaces.</summary>
public sealed record CreateRoleInput(
    [Description(Holds.RoleName)] string? Name = null,
    [Description(Holds.RolePermissions)] string[]? Permissions = null);

/// <summary>What changing a role sends, as a form or one JSON object.</summary>
public sealed record ChangeRoleInput(
    [Description(Holds.ChangedRole)] string? Role = null,
    [Description(Holds.ChangedRoleName)] string? Name = null,
    [Description(Holds.RolePermissions)] string[]? Permissions = null,
    [Description(Holds.RoleMove)] string? Move = null);

/// <summary>What deleting a role sends, as a form or one JSON object.</summary>
public sealed record DeleteRoleInput([Description(Holds.DeletedRole)] string? Role = null);

/// <summary>The place's roles, a section of its settings at <c>/settings/roles</c>, for whoever holds
/// <see cref="Bit.ManageRoles"/>. Its <c>GET</c> is one page of the roles in Koan's order (<see cref="RolesPage"/>), the
/// highest first, one resource both ways, by <c>page</c>; a reader who may not shape them is refused with the gate,
/// <c>401</c> to sign in and <c>403</c> otherwise: a page reads it with no role listed, and a JSON request as its problem.
/// Its <c>POST</c>s take their fields as a form or a JSON
/// object and send one command: <c>/settings/roles</c> creates a role (<see cref="CreateRole"/>) from <c>name</c> and
/// <c>permissions</c>; <c>/change</c> changes the role whose word <c>role</c> names (<see cref="ReviseRole"/>) by any of
/// <c>name</c>, <c>permissions</c> and <c>move</c>; <c>/delete</c> deletes it (<see cref="DeleteRole"/>). A form is sent
/// back to the roles; a JSON request reads the role as the page now lists it, created (<c>201</c>) or changed
/// (<c>200</c>), or nothing for a deletion (<c>204</c>); a refusal reads the page again with the reason above the list, or
/// a problem. A reader for whom the place does not exist receives what an address with nothing there receives.</summary>
[Managing]
public sealed class RolesController(KoanWebBaseUrl baseUrl, RoleCollection roles) : ControllerBase
{
    public const string Path = "/settings/roles";

    public const string ChangePath = Path + "/change";

    public const string DeletePath = Path + "/delete";

    public const string NameField = "name";

    public const string PermissionsField = "permissions";

    /// <summary>The field that names the role changed or deleted, by its word.</summary>
    public const string RoleField = "role";

    public const string MoveField = "move";

    /// <summary>The query parameter that names the page of the roles to read, from 1.</summary>
    public const string PageQuery = "page";

    /// <summary>The most roles one page holds.</summary>
    public const int PageSize = 50;

    public static readonly Reason MoveRule = Reason.Invalid(MoveField, "A role moves higher or lower.");

    /// <summary>The moves a form offers, as words.</summary>
    public static readonly IReadOnlyList<string> Moves = Enum.GetValues<RoleMove>().Select(Word).ToList();

    public static string Word(RoleMove move) => move.ToString().ToLowerInvariant();

    /// <summary>The page a <c>page</c> parameter names, from 1: one that is no such number reads the first, since it is all a
    /// hand-edited address could want.</summary>
    public static int Numbered(string? page)
        => int.TryParse(page, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : 1;

    [HttpGet(Path)]
    [Tool(Name = "read_roles")]
    [Description("Read the roles in their order, the highest first, a page at a time, for whoever holds ManageRoles. Follow with create_role, change_role or delete_role where one's can allows it.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> Get([FromQuery(Name = PageQuery), Description(Holds.Number)] string? page, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = Here.Of(HttpContext).Around;
        if (place.Gate(Bit.ManageRoles) is { } refused && PostWrites.WantsJson(Request)) return this.Refused(refused);
        var read = await Page(place, personId, Numbered(page), ct);
        return place.Gate(Bit.ManageRoles) is { } gate ? new ObjectResult(read) { StatusCode = Answers.Status(HttpContext, gate) } : Ok(read);
    }

    [HttpPost(Path)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "create_role", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create a role by naming it and choosing what it grants, for whoever holds ManageRoles; no one is in it yet. Sending it again makes a second role. Follow with give_role.")]
    public Task<IActionResult> Create(CreateRoleInput input, CancellationToken ct) => Write(input.Name, input.Permissions,
        Granted(input.Permissions, out var bits) is { } refused
            ? refused
            : new CreateRole(PostWrites.PersonId(User) ?? "", input.Name ?? "", bits ?? Bit.None), created: true, ct);

    [HttpPost(ChangePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "change_role", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Rename a role, change what it grants or move it one place, for whoever holds ManageRoles and manages the role. What it grants changes for everyone in it, and sending a move again moves it again.")]
    public Task<IActionResult> Change(ChangeRoleInput input, CancellationToken ct) => Write(input.Name, input.Permissions, Revising(input), created: false, ct);

    private object Revising(ChangeRoleInput input)
    {
        if (Granted(input.Permissions, out var bits) is { } refused) return refused;
        var move = Tangents.TangentWrites.Choice(input.Move, Moves, out var moveValid);
        if (!moveValid) return MoveRule;
        if (Roles.KeyOf(input.Role) is not { } key) return RoleSettings.NoRole;
        return new ReviseRole(PostWrites.PersonId(User) ?? "", key, input.Name, bits, move is { } chosen ? Enum.GetValues<RoleMove>()[chosen] : null);
    }

    [HttpPost(DeletePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "delete_role", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Delete a role the place created, for whoever holds ManageRoles, manages it and holds what it grants. Everyone in it loses what it granted.")]
    public Task<IActionResult> Delete(DeleteRoleInput input, CancellationToken ct) => Write(null, null,
        Roles.KeyOf(input.Role) is { } key
            ? new DeleteRole(PostWrites.PersonId(User) ?? "", key)
            : RoleSettings.NoRole, created: false, ct);

    /// <summary>The bits <paramref name="sent"/> names, in <paramref name="bits"/>, null where nothing was sent; the refusal
    /// where a word names no bit, null otherwise.</summary>
    private static Reason? Granted(string[]? sent, out Bit? bits)
    {
        bits = null;
        if (sent is null) return null;
        bits = RoleSettings.Parse(string.Join(' ', sent.Where(word => !string.IsNullOrEmpty(word))));
        return bits is null ? RoleSettings.PermissionsRule : null;
    }

    /// <summary>One change: the place; then <paramref name="built"/>, the command to send or the refusal
    /// of what was sent, whose name and permissions a form keeps; then the command's outcome.</summary>
    private async Task<IActionResult> Write(string? name, string[]? permissions, object built, bool created, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);

        if (built is Reason refused) return await Refused(personId, name, permissions, refused, created, ct);
        var sent = (ICommand<Role>)built;
        var outcome = await sent.Send();
        switch (outcome)
        {
            case Outcome<Role>.Done { Result: var role }:
                if (!PostWrites.WantsJson(Request))
                {
                    Response.Headers.Location = BaseAddress() + Path;
                    return StatusCode(StatusCodes.Status303SeeOther);
                }

                if (sent is DeleteRole) return NoContent();
                var reader = await Capabilities.Load(personId, null, null, ct);
                var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
                var listed = Listed(await roles.Get(role.Key, ct) ?? role, reader, personId, space, (await roles.Page(pageSize: 1, ct: ct)).TotalCount);
                return new ObjectResult(listed)
                {
                    StatusCode = created ? StatusCodes.Status201Created : StatusCodes.Status200OK,
                    ContentTypes = { "application/json" }
                };
            case Outcome<Role>.Refused { Reason: var reason }:
                return await Refused(personId, name, permissions, reason, created, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the page again, read afresh,
    /// with the reason above the list, keeping a new role's name and permissions as they were sent.</summary>
    private async Task<IActionResult> Refused(string? personId, string? name, string[]? permissions, Reason reason, bool created, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);
        var page = await Page(place, personId, 1, ct);
        if (page.Gate is null)
        {
            page = page with { Failure = reason.Text };
            if (created)
            {
                page = page with
                {
                    Name = name ?? "",
                    Chosen = (permissions ?? []).OfType<string>().SelectMany(word => word.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)).ToList()
                };
            }
        }

        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    private async Task<RolesPage> Page(Capabilities place, string? personId, int number, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var here = baseUrl.PathBase + Path + (number > 1 ? "?" + PageQuery + "=" + number : "");
        var gate = place.Gate(Bit.ManageRoles)?.Word;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        var read = gate is null ? await roles.Page(page: number, pageSize: PageSize, ct: ct) : null;
        var listed = read is null ? [] : read.Items.Select(role => Listed(role, place, personId, space, read.TotalCount)).ToList();
        return new RolesPage(
            TangentSpaceSettingsController.Sections(place, baseAddress, TangentSpaceSettingsController.RolesSection),
            listed,
            Bits.Each.Select(bit => new RolesPage.Permission(bit.ToString(), RoleSettings.Plain(bit), People.Holds(place, bit))).ToList(),
            read is not null && (long)number * PageSize < read.TotalCount ? baseAddress + Path + "?" + PageQuery + "=" + (number + 1) : null,
            number > 1 ? baseAddress + Path + (number > 2 ? "?" + PageQuery + "=" + (number - 1) : "") : null,
            gate is null ? Forms(baseAddress) : null,
            place.Can,
            gate,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct));
    }

    /// <summary>One role as the reader of the roles finds it, the order holding <paramref name="count"/> roles: what they
    /// may do to it by <see cref="People.Manages"/>, nothing to everyone, never a move past either end, and never a
    /// deletion of a seeded role or of one granting a permission they do not hold.</summary>
    public static RolesPage.Listed Listed(Role role, Capabilities reader, string? readerId, TangentSpace space, long count)
    {
        var can = new List<string>();
        if (role.Key != Roles.Everyone && reader.Holds(Bit.ManageRoles) && People.Manages(reader, readerId, space, role))
        {
            can.Add(RolesPage.Verb.Change);
            if (role.Position < count - 1 && People.Manages(reader, readerId, space, role, role.Position + 1)) can.Add(RolesPage.Verb.Higher);
            if (role.Position > 1 && People.Manages(reader, readerId, space, role, role.Position - 1)) can.Add(RolesPage.Verb.Lower);
            if (!Roles.Seeded(role.Key) && People.Holding(reader, role) is null) can.Add(RolesPage.Verb.Delete);
        }

        return new RolesPage.Listed(Roles.Word(role.Key), role.Name, Bits.Names(Bits.InTokens(role.Permissions)),
            Roles.Seeded(role.Key), can);
    }

    /// <summary>The forms that shape the roles, under <paramref name="baseAddress"/>.</summary>
    private static RoleForms Forms(string baseAddress)
    {
        return new RoleForms(
            Form.Posting(baseAddress + Path,
                new Form.Field(NameField, Holds.RoleName),
                new Form.Field(PermissionsField, Holds.RolePermissions + " Left out, none.")),
            Form.Posting(baseAddress + ChangePath,
                new Form.Field(RoleField, Holds.ChangedRole),
                new Form.Field(NameField, Holds.ChangedRoleName),
                new Form.Field(PermissionsField, Holds.RolePermissions + " Left out, they stay."),
                new Form.Field(MoveField, Holds.RoleMove)),
            Form.Posting(baseAddress + DeletePath,
                new Form.Field(RoleField, Holds.DeletedRole)));
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
