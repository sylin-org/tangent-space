using System.Net;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Tools;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Access;

/// <summary>A route declares the bit it needs and the Doorway judges it before anything is bound: another bit that lets the
/// reader in as well (<see cref="NeedsAttribute.AnyOf"/>), a bit asked of the place where the address names a Tangent
/// (<see cref="NeedsAttribute.Place"/>), and a write's own bit, so what was sent is never read for someone who may not.</summary>
public sealed class DeclaredGateTests
{
    private const string Keeper = "role:keeper";

    [Fact]
    public async Task Whoever_manages_roles_alone_reads_the_people_and_whoever_manages_neither_standing_nor_roles_is_refused_with_the_standing_gate()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await tangentSpace.Services.GetRequiredService<RoleCollection>().Define(Keeper, "Keeper", Bits.Tokens(Bit.View | Bit.ManageRoles), ct: Cancellation);
        var keeper = Persona.Id("kim4gates");
        await Given.AParticipant(tangentSpace, keeper, [Roles.Member, Keeper], ct: Cancellation);
        using var kim = await SignedIn(tangentSpace, keeper);
        using var kimTools = await ToolClient.Connect(tangentSpace, kim);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var adaTools = await ToolClient.Connect(tangentSpace, ada);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var moTools = await ToolClient.Connect(tangentSpace, mo);

        var byRoles = await kimTools.Call("read_people");
        var byStanding = await moTools.Call("read_people");
        var byNeither = await adaTools.Call("read_people");
        var page = await Send(ada.Client, HttpMethod.Get, Participants.PeopleController.Path);

        Assert.Equal((200, false), (byRoles.Status, byRoles.Failed));
        Assert.Equal((200, false), (byStanding.Status, byStanding.Failed));
        var gate = Capabilities.NoRoleGrants(Bit.ManageStanding).Word;
        Assert.Equal((403, true, gate), (byNeither.Status, byNeither.Failed, byNeither.Structured!.Value.GetProperty("word").GetString()));
        Assert.Contains($"class=\"refusal\" data-word=\"{gate}\"", page.Body);
    }

    /// <summary>The people, the applications, the roles and export each answer whoever may not read them as the Doorway
    /// refuses, the refusal's page with the gate's word for a browser, its problem for JSON and the sign-in for a visitor;
    /// whoever may reads the section, and a form refused there reads it again, as the Doorway let them in, with the reason
    /// above the list.</summary>
    [Fact]
    public async Task Each_settings_section_refuses_whoever_may_not_read_it_and_a_refused_form_reads_it_again_with_the_reason()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var operating = Persona.Id("lea4gates");
        await Given.AParticipant(tangentSpace, operating, [Roles.Administrator], ct: Cancellation);
        using var lea = await SignedIn(tangentSpace, operating);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var visitor = Agent(tangentSpace);

        foreach (var (path, bit) in new[]
                 {
                     (Participants.PeopleController.Path, Bit.ManageStanding), (Participants.ApplicationsController.Path, Bit.ManageStanding),
                     (RolesController.Path, Bit.ManageRoles), (Lifecycle.ExportController.Path, Bit.ManageSpace)
                 })
        {
            var gate = Capabilities.NoRoleGrants(bit).Word;
            var page = await Send(ada.Client, HttpMethod.Get, path);
            var problem = await Send(ada.Client, HttpMethod.Get, path, json: true);
            var visiting = await Send(visitor, HttpMethod.Get, path);
            var held = await Send(lea.Client, HttpMethod.Get, path, json: true);

            Assert.Equal(System.Net.HttpStatusCode.Forbidden, page.Status);
            Assert.Contains($"class=\"refusal\" data-word=\"{gate}\"", page.Body);
            Assert.Equal((System.Net.HttpStatusCode.Forbidden, gate), (problem.Status, problem.Word));
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, visiting.Status);
            Assert.Contains("/sign-in?return=", visiting.Body);
            Assert.Equal(System.Net.HttpStatusCode.OK, held.Status);
            Assert.False(held.Json.TryGetProperty("gate", out _));
        }

        var named = new string('x', RoleSettings.NameLimit + 1);
        var role = await Form(lea, RolesController.Path, RolesController.Path,
            new Dictionary<string, string> { [RolesController.NameField] = named, [RolesController.PermissionsField] = nameof(Bit.Post) });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, role.Status);
        Assert.Contains(RoleSettings.NameRule.Text, role.Body);
        Assert.Contains($"value=\"{named}\"", role.Body);
        Assert.Contains("Moderator", role.Body);

        var decided = await Form(lea, Participants.ApplicationsController.Path, Participants.PeopleController.StandingPath,
            new Dictionary<string, string>
            {
                [Participants.PeopleController.ParticipantField] = Persona.Id("nobody4gates"), [Participants.PeopleController.ToField] = "declined",
                [Render.Form.ReturnField] = Participants.ApplicationsController.Path
            });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, decided.Status);
        Assert.Contains(Koan.Web.Controllers.Answers.Nothing, decided.Body);
    }

    [Fact]
    public async Task Deleting_a_tangent_asks_manage_space_of_the_place_of_whoever_manages_the_tangent_before_reading_what_they_sent()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await tangentSpace.Services.GetRequiredService<RoleCollection>()
            .Overwrite(Overwrites.Resource(Workshop), OverwriteSubjectKind.Person, Ada, [Bits.Token(Bit.ManageTangent)], [], ct: Cancellation);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var tools = await ToolClient.Connect(tangentSpace, ada);

        var renamed = await tools.Call("change_tangent", new { tangent = Workshop, name = "Ada's workshop" });
        // A count the workshop does not hold would be refused as a conflict, were it read.
        var deleted = await tools.Call("delete_tangent", new { tangent = Workshop, topics = 7 });
        var unread = await Send(ada.Client, HttpMethod.Delete, "/t/" + Workshop + "?topics=all+of+them", json: true);
        var hidden = await tools.Call("delete_tangent", new { tangent = Studio });

        Assert.Equal((200, false), (renamed.Status, renamed.Failed));
        Assert.Equal((403, Capabilities.NoRoleGrants(Bit.ManageSpace).Word), (deleted.Status, deleted.Structured!.Value.GetProperty("word").GetString()));
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NoRoleGrants(Bit.ManageSpace).Word), (unread.Status, unread.Word));
        Assert.Equal(404, hidden.Status);
        Assert.Equal("Ada's workshop", (await Tangents.Tangent.Get(Workshop, Cancellation))!.Name);
    }

    [Fact]
    public async Task A_write_without_its_bit_is_refused_before_what_was_sent_is_read()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var tools = await ToolClient.Connect(tangentSpace, ada);

        var admission = await tools.Call("change_admission", new { admission = "neither" });
        var role = await tools.Call("create_role", new { name = "Mine", permissions = new[] { "Nothing" } });
        var banned = await tools.Call("set_standing", new { participant = Bea, to = "banned" });

        Assert.Equal((403, Capabilities.NoRoleGrants(Bit.ManageSpace).Word), (admission.Status, admission.Structured!.Value.GetProperty("word").GetString()));
        Assert.Equal((403, Capabilities.NoRoleGrants(Bit.ManageRoles).Word), (role.Status, role.Structured!.Value.GetProperty("word").GetString()));
        Assert.Equal((403, Capabilities.NoRoleGrants(Bit.ManageStanding).Word), (banned.Status, banned.Structured!.Value.GetProperty("word").GetString()));
    }
}
