using System.Net;
using System.Text.Json;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Participants;
using Tangent.Space;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Access;

/// <summary>The place's roles at <c>/settings/roles</c>, for whoever holds ManageRoles: a role is created with a name and
/// what it grants in plain words, directly above the member role; it moves one place higher or lower in Koan's order;
/// its name and what it grants change; it is given and taken on People as the seeded roles are; and it is deleted with an
/// inline confirmation, with its members. Whoever shapes roles changes only those below their own highest and gives or
/// takes away only what they hold.</summary>
public sealed class RoleSettingsTests
{
    private const string Leo = "did:plc:leo4roles";
    private const string Otto = "did:plc:otto4roles";

    /// <summary>The writes' place, with Leo an administrator and Otto its operator.</summary>
    private static async Task<WriteLoop> Shaped(TangentSpaceFixture tangentSpace)
    {
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Member, Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Otto, [Roles.Member, Roles.Administrator], ct: Cancellation);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.OperatorId = Otto;
        space.ProprietorId = Otto;
        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Store<TangentSpace>(space), Cancellation));
        return writer;
    }

    /// <summary>The roles a page lists, by word, in their order.</summary>
    private static IReadOnlyList<string?> Order(JsonElement page)
        => page.GetProperty("roles").EnumerateArray().Select(role => role.GetProperty("word").GetString()).ToList();

    /// <summary>The row a page lists for the role with <paramref name="word"/>.</summary>
    private static JsonElement Row(JsonElement page, string word)
        => page.GetProperty("roles").EnumerateArray().Single(role => role.GetProperty("word").GetString() == word);

    private static IReadOnlyList<string?> Strings(JsonElement element, string name)
        => element.GetProperty(name).EnumerateArray().Select(item => item.GetString()).ToList();

    private static async Task<int> PositionOf(string key) => (await Role.Get(Role.KeyFor(key), Cancellation))!.Position;

    [Fact]
    public async Task An_administrator_creates_a_role_moves_it_changes_it_gives_it_on_people_and_deletes_it_with_its_members()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Shaped(tangentSpace);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        using var leo = await SignedIn(tangentSpace, Leo);
        var path = RolesController.Path;

        // The roles in their order, the highest first, and the section in the rail.
        var page = await Send(leo.Client, HttpMethod.Get, path, json: true);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(["administrator", "moderator", "member", "everyone"], Order(page.Json));
        Assert.Contains("Roles", page.Json.GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("name").GetString()));
        Assert.Empty(Strings(Row(page.Json, "administrator"), "can"));
        Assert.Empty(Strings(Row(page.Json, "everyone"), "can"));
        Assert.Contains(RolesPage.Verb.Change, Strings(Row(page.Json, "moderator"), "can"));
        Assert.DoesNotContain(RolesPage.Verb.Delete, Strings(Row(page.Json, "moderator"), "can"));

        // Created from the page's form, named, granting what was checked, directly above the member role.
        var created = await Form(leo, path, path, new Dictionary<string, string>
        {
            [RolesController.NameField] = "Night shift", [RolesController.PermissionsField] = "Moderate ManageStanding"
        });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + path), (created.Status, created.Location));
        var role = (await roles.Get("role:night-shift", Cancellation))!;
        Assert.Equal(("Night shift", Bit.Moderate | Bit.ManageStanding), (role.Name, Bits.InTokens(role.Permissions)));
        Assert.Equal(await PositionOf(Roles.Member) + 1, role.Position);
        page = await Send(leo.Client, HttpMethod.Get, path, json: true);
        Assert.Equal(["administrator", "moderator", "night-shift", "member", "everyone"], Order(page.Json));
        Assert.Equal([nameof(Bit.Moderate), nameof(Bit.ManageStanding)], Strings(Row(page.Json, "night-shift"), "permissions"));
        var html = WebUtility.HtmlDecode((await Send(leo.Client, HttpMethod.Get, path)).Body);
        Assert.Contains("Night shift", html);
        Assert.Contains("Moderate: edit, remove, lock, pin and move anyone’s posts", html);
        Assert.Contains("Everyone in it loses it", html);

        // One place higher, past the moderator role.
        var moved = await Json(leo, path, RolesController.ChangePath, new { role = "night-shift", move = "higher" });
        Assert.Equal(HttpStatusCode.OK, moved.Status);
        Assert.Equal(["administrator", "night-shift", "moderator", "member", "everyone"], Order((await Send(leo.Client, HttpMethod.Get, path, json: true)).Json));

        // Renamed and given other permissions; its word stays.
        var changed = await Json(leo, path, RolesController.ChangePath, new { role = "night-shift", name = "Night watch", permissions = new[] { "View", "Post" } });
        Assert.Equal((HttpStatusCode.OK, "night-shift", "Night watch"),
            (changed.Status, changed.Json.GetProperty("word").GetString(), changed.Json.GetProperty("name").GetString()));
        Assert.Equal(Bit.View | Bit.Post, Bits.InTokens((await roles.Get("role:night-shift", Cancellation))!.Permissions));

        // Given on People as the seeded roles are, and listed among her roles.
        Assert.Contains("night-shift", Strings((await Send(leo.Client, HttpMethod.Get, PeopleController.Path, json: true)).Json
            .GetProperty("people").EnumerateArray().Single(row => row.GetProperty("id").GetString() == Ada), "give"));
        var given = await Json(leo, PeopleController.Path, PeopleController.GivePath, new { participant = Ada, role = "night-shift" });
        Assert.Equal(HttpStatusCode.OK, given.Status);
        Assert.Contains("Night watch", Strings(given.Json, "roles"));
        Assert.True((await roles.Bag(Ada, authenticated: true, Cancellation)).InRole("role:night-shift"));

        // A seeded role stays.
        var seeded = await Json(leo, path, RolesController.DeletePath, new { role = "moderator" });
        Assert.Equal((HttpStatusCode.BadRequest, RoleSettings.SeededStays), (seeded.Status, seeded.Json.GetProperty("detail").GetString()));

        // Deleted from the inline confirmation's form: gone, and gone from everyone who held it.
        var deleted = await Form(leo, path, RolesController.DeletePath, new Dictionary<string, string> { [RolesController.RoleField] = "night-shift" });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + path), (deleted.Status, deleted.Location));
        Assert.Null(await roles.Get("role:night-shift", Cancellation));
        Assert.False((await roles.Bag(Ada, authenticated: true, Cancellation)).InRole("role:night-shift"));
        Assert.Equal(["administrator", "moderator", "member", "everyone"], Order((await Send(leo.Client, HttpMethod.Get, path, json: true)).Json));
        Assert.Equal(HttpStatusCode.NotFound, (await Json(leo, path, RolesController.DeletePath, new { role = "night-shift" })).Status);
    }

    [Fact]
    public async Task A_moderator_who_shapes_roles_changes_only_those_below_their_own_highest_and_gives_nothing_they_do_not_hold()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Shaped(tangentSpace);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await roles.SetPermissions(Roles.Moderator, Bits.Tokens(Roles.ModeratorBits | Bit.ManageRoles), Cancellation);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var ada = await SignedIn(tangentSpace, Ada);
        var path = RolesController.Path;

        // A member shapes nothing: the gate, and no role listed.
        var member = await Send(ada.Client, HttpMethod.Get, path, json: true);
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NoRoleGrants(Bit.ManageRoles)), (member.Status, member.Json.GetProperty("gate").GetString()));
        Assert.Empty(member.Json.GetProperty("roles").EnumerateArray());

        // The moderator creates a role below their own, granting what they hold.
        var created = await Json(mo, path, path, new { name = "Helpers", permissions = "View Post Topic" });
        Assert.Equal((HttpStatusCode.Created, "helpers"), (created.Status, created.Json.GetProperty("word").GetString()));
        Assert.True(await PositionOf("role:helpers") < await PositionOf(Roles.Moderator));
        var page = (await Send(mo.Client, HttpMethod.Get, path, json: true)).Json;
        Assert.Equal(["administrator", "moderator", "helpers", "member", "everyone"], Order(page));
        Assert.Empty(Strings(Row(page, "administrator"), "can"));
        Assert.Empty(Strings(Row(page, "moderator"), "can"));
        Assert.Equal([RolesPage.Verb.Change, RolesPage.Verb.Lower, RolesPage.Verb.Delete], Strings(Row(page, "helpers"), "can"));
        Assert.Equal([nameof(Bit.ManageTangent), nameof(Bit.ManageSpace), nameof(Bit.Administrator)], page.GetProperty("permissions").EnumerateArray()
            .Where(permission => !permission.GetProperty("held").GetBoolean()).Select(permission => permission.GetProperty("bit").GetString()));

        // At their own highest and above it, refused: no change, no move into it, no giving it.
        foreach (var word in new[] { "moderator", "administrator" })
        {
            var refused = await Json(mo, path, RolesController.ChangePath, new { role = word, name = "Stewards" });
            Assert.Equal((HttpStatusCode.Forbidden, RoleSettings.NotManaged), (refused.Status, refused.Gate));
        }

        var raised = await Json(mo, path, RolesController.ChangePath, new { role = "helpers", move = "higher" });
        Assert.Equal((HttpStatusCode.Forbidden, RoleSettings.NotManaged), (raised.Status, raised.Gate));
        Assert.Equal("Moderator", (await roles.Get(Roles.Moderator, Cancellation))!.Name);
        Assert.True(await PositionOf("role:helpers") < await PositionOf(Roles.Moderator));
        var notGiven = await Json(mo, PeopleController.Path, PeopleController.GivePath, new { participant = Ada, role = People.Moderator });
        Assert.Equal((HttpStatusCode.Forbidden, People.NotManaged), (notGiven.Status, notGiven.Gate));

        // Nothing they do not hold is granted, on a new role or one below them.
        var unheld = await Json(mo, path, path, new { name = "Keepers", permissions = "ManageSpace" });
        Assert.Equal((HttpStatusCode.Forbidden, People.NotHeld), (unheld.Status, unheld.Gate));
        Assert.Null(await roles.Get("role:keepers", Cancellation));
        var widened = await Json(mo, path, RolesController.ChangePath, new { role = "member", permissions = "View Post Topic EditOwn RemoveOwn ManageSpace" });
        Assert.Equal((HttpStatusCode.Forbidden, People.NotHeld), (widened.Status, widened.Gate));

        // Below them, theirs to give.
        Assert.Equal(HttpStatusCode.OK, (await Json(mo, PeopleController.Path, PeopleController.GivePath, new { participant = Ada, role = "helpers" })).Status);
        Assert.True((await roles.Bag(Ada, authenticated: true, Cancellation)).InRole("role:helpers"));
    }
}
