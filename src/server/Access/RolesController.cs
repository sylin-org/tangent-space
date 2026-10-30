using Koan.Identity.Roles;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Access;

/// <summary>The place's roles, a section of its settings at <c>/settings/roles</c>, for whoever holds
/// <see cref="Bit.ManageRoles"/>. Its <c>GET</c> is one page of the roles in Koan's order (<see cref="RolesPage"/>), the
/// highest first, one resource both ways, by <c>page</c>; a reader who may not shape them reads the gate with no role
/// listed, answered <c>401</c> to sign in and <c>403</c> otherwise. Its <c>POST</c>s take their fields as a form or a JSON
/// object and send one command: <c>/settings/roles</c> creates a role (<see cref="CreateRole"/>) from <c>name</c> and
/// <c>permissions</c>; <c>/change</c> changes the role whose word <c>role</c> names (<see cref="ReviseRole"/>) by any of
/// <c>name</c>, <c>permissions</c> and <c>move</c>; <c>/delete</c> deletes it (<see cref="DeleteRole"/>). A form is sent
/// back to the roles; a JSON request reads the role as the page now lists it, created (<c>201</c>) or changed
/// (<c>200</c>), or nothing for a deletion (<c>204</c>); a refusal reads the page again with the reason above the list, or
/// a problem. A reader for whom the place does not exist receives what an address with nothing there receives.</summary>
public sealed class RolesController(KoanWebBaseUrl baseUrl, WriteLoop writer, RoleCollection roles) : ControllerBase
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

    public const string MoveRule = "A role moves higher or lower.";

    /// <summary>The moves a form offers, as words.</summary>
    public static readonly IReadOnlyList<string> Moves = Enum.GetValues<RoleMove>().Select(Word).ToList();

    public static string Word(RoleMove move) => move.ToString().ToLowerInvariant();

    [HttpGet(Path)]
    public async Task<IActionResult> Get([FromQuery(Name = PageQuery)] int? page, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        var read = await Page(place, personId, Math.Max(page ?? 1, 1), ct);
        return read.Gate is { } gate ? new ObjectResult(read) { StatusCode = PostWrites.Status(gate) } : Ok(read);
    }

    [HttpPost(Path)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public Task<IActionResult> Create(CancellationToken ct) => Write(fields =>
        Granted(fields, out var bits) is { } refused
            ? refused
            : new CreateRole(PostWrites.PersonId(User) ?? "", fields.GetValueOrDefault(NameField) ?? "", bits ?? Bit.None), created: true, ct);

    [HttpPost(ChangePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public Task<IActionResult> Change(CancellationToken ct) => Write(fields =>
    {
        if (Granted(fields, out var bits) is { } refused) return refused;
        var move = Tangents.TangentWrites.Choice(fields, MoveField, Moves, out var moveValid);
        if (!moveValid) return MoveRule;
        if (Roles.KeyOf(fields.GetValueOrDefault(RoleField)) is not { } key) return RoleSettings.NoRole;
        return new ReviseRole(PostWrites.PersonId(User) ?? "", key, fields.GetValueOrDefault(NameField), bits,
            move is { } chosen ? Enum.GetValues<RoleMove>()[chosen] : null);
    }, created: false, ct);

    [HttpPost(DeletePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public Task<IActionResult> Delete(CancellationToken ct) => Write(fields =>
        Roles.KeyOf(fields.GetValueOrDefault(RoleField)) is { } key
            ? new DeleteRole(PostWrites.PersonId(User) ?? "", key)
            : RoleSettings.NoRole, created: false, ct);

    /// <summary>The bits <c>permissions</c> names, in <paramref name="bits"/>, null where it was not sent; the refusal
    /// where a word names no bit, null otherwise.</summary>
    private static string? Granted(IReadOnlyDictionary<string, string> fields, out Bit? bits)
    {
        bits = null;
        if (fields.GetValueOrDefault(PermissionsField) is not { } sent) return null;
        bits = RoleSettings.Parse(sent);
        return bits is null ? RoleSettings.PermissionsRule : null;
    }

    /// <summary>One change: the form's token, the place, the fields; the command <paramref name="command"/> builds from
    /// them, or the refusal it names for what was sent; then the writer's outcome.</summary>
    private async Task<IActionResult> Write(Func<IReadOnlyDictionary<string, string>, object> command, bool created, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();

        var built = command(fields);
        if (built is string unsent) return await Refused(personId, fields, unsent, created, ct);
        var sent = (ICommand<Role>)built;
        var outcome = await writer.Send(sent, ct);
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
            case Outcome<Role>.Refused { Reason: ChangeTangentSpace.NoPlace }:
                return PostWrites.Absent();
            case Outcome<Role>.Refused { Reason: var reason }:
                return await Refused(personId, fields, reason, created, ct);
            case Outcome<Role>.Failed { Cause: var cause }:
                throw new InvalidOperationException($"{sent.GetType().Name} failed.", cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for {sent.GetType().Name}.");
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the page again, read afresh,
    /// with the reason above the list, keeping a new role's name and permissions as they were sent.</summary>
    private async Task<IActionResult> Refused(string? personId, IReadOnlyDictionary<string, string> fields, string reason, bool created,
        CancellationToken ct)
    {
        var status = Status(reason);
        var gate = !Rule(reason) && status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden;
        if (PostWrites.WantsJson(Request))
        {
            if (gate) return PostWrites.Gated(reason);
            return Rule(reason)
                ? new Problem(status, "Not yours to change", reason, reason).ToResult()
                : new Problem(status, "Not changed", reason).ToResult();
        }

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        var page = await Page(place, personId, 1, ct);
        if (page.Gate is null)
        {
            page = page with { Failure = gate ? PostWrites.Sentence(reason) : reason };
            if (created)
            {
                page = page with
                {
                    Name = fields.GetValueOrDefault(NameField) ?? "",
                    Chosen = (fields.GetValueOrDefault(PermissionsField) ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
                };
            }
        }

        return new ObjectResult(page) { StatusCode = status };
    }

    /// <summary>The status a refusal answers with: <c>404</c> for a role no one has, <c>403</c> for the rule of who
    /// changes which role, <c>400</c> for what was sent, and a gate's own otherwise.</summary>
    private static int Status(string reason) => reason switch
    {
        RoleSettings.NoRole => StatusCodes.Status404NotFound,
        _ when Rule(reason) => StatusCodes.Status403Forbidden,
        RoleSettings.NameRule or RoleSettings.PermissionsRule or RoleSettings.EveryoneStays or RoleSettings.SeededStays or RoleSettings.Administers
            or RoleSettings.NoWord or MoveRule => StatusCodes.Status400BadRequest,
        _ => PostWrites.Status(reason)
    };

    /// <summary>Whether a refusal is the rule of who changes which role, rather than a missing verb or what was sent.</summary>
    private static bool Rule(string reason) => reason is RoleSettings.NotManaged or People.NotHeld;

    private async Task<RolesPage> Page(Capabilities place, string? personId, int number, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var here = baseUrl.PathBase + Path + (number > 1 ? "?" + PageQuery + "=" + number : "");
        var gate = place.Gate(Bit.ManageRoles);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        var read = gate is null ? await roles.Page(page: number, pageSize: PageSize, ct: ct) : null;
        var listed = read is null ? [] : read.Items.Select(role => Listed(role, place, personId, space, read.TotalCount)).ToList();
        var token = gate is null ? FormToken.For(HttpContext) : null;
        return new RolesPage(
            TangentSpaceSettingsController.Sections(place, baseAddress, TangentSpaceSettingsController.RolesSection),
            listed,
            Bits.Each.Select(bit => new RolesPage.Permission(bit.ToString(), RoleSettings.Plain(bit), People.Holds(place, bit))).ToList(),
            read is not null && (long)number * PageSize < read.TotalCount ? baseAddress + Path + "?" + PageQuery + "=" + (number + 1) : null,
            number > 1 ? baseAddress + Path + (number > 2 ? "?" + PageQuery + "=" + (number - 1) : "") : null,
            token is null ? null : Forms(baseAddress, token),
            place.Can,
            gate,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct));
    }

    /// <summary>One role as the reader of the roles finds it, the order holding <paramref name="count"/> roles: what they
    /// may do to it by <see cref="People.Manages"/>, nothing to everyone, never a move past either end, and never a
    /// deletion of a seeded role.</summary>
    public static RolesPage.Listed Listed(Role role, Capabilities reader, string? readerId, TangentSpace space, long count)
    {
        var can = new List<string>();
        if (role.Key != Roles.Everyone && reader.Holds(Bit.ManageRoles) && People.Manages(reader, readerId, space, role))
        {
            can.Add(RolesPage.Verb.Change);
            if (role.Position < count - 1 && People.Manages(reader, readerId, space, role, role.Position + 1)) can.Add(RolesPage.Verb.Higher);
            if (role.Position > 1 && People.Manages(reader, readerId, space, role, role.Position - 1)) can.Add(RolesPage.Verb.Lower);
            if (!Roles.Seeded(role.Key)) can.Add(RolesPage.Verb.Delete);
        }

        return new RolesPage.Listed(Roles.Word(role.Key), role.Name, Bits.Names(Bits.InTokens(role.Permissions)),
            Roles.Seeded(role.Key), can);
    }

    /// <summary>The forms that shape the roles, under <paramref name="baseAddress"/>.</summary>
    private static RoleForms Forms(string baseAddress, FormToken token)
    {
        var permissions = "The bits it grants, by name, separated by spaces, among those whose held is true; sent empty, none.";
        return new RoleForms(
            Form.Posting(baseAddress + Path, token,
                new Form.Field(NameField, $"The role's name, 1 to {RoleSettings.NameLimit} characters; its word derives from it and never changes."),
                new Form.Field(PermissionsField, permissions + " Left out, none.")),
            Form.Posting(baseAddress + ChangePath, token,
                new Form.Field(RoleField, "The word of a role listed here with change in its can."),
                new Form.Field(NameField, $"Its name, 1 to {RoleSettings.NameLimit} characters; left out, it stays."),
                new Form.Field(PermissionsField, permissions + " Left out, they stay."),
                new Form.Field(MoveField, "higher or lower, one place, where its can holds that word; left out, it stays.")),
            Form.Posting(baseAddress + DeletePath, token,
                new Form.Field(RoleField, "The word of a role listed here with delete in its can.")));
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
