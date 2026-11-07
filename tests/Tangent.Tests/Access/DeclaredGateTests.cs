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
        var deleted = await tools.Call("delete_tangent", new { tangent = Workshop, topics = "all of them" });
        var hidden = await tools.Call("delete_tangent", new { tangent = Studio });

        Assert.Equal((200, false), (renamed.Status, renamed.Failed));
        Assert.Equal((403, Capabilities.NoRoleGrants(Bit.ManageSpace).Word), (deleted.Status, deleted.Structured!.Value.GetProperty("word").GetString()));
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
        var banned = await tools.Call("ban", new { participant = Bea });

        Assert.Equal((403, Capabilities.NoRoleGrants(Bit.ManageSpace).Word), (admission.Status, admission.Structured!.Value.GetProperty("word").GetString()));
        Assert.Equal((403, Capabilities.NoRoleGrants(Bit.ManageRoles).Word), (role.Status, role.Structured!.Value.GetProperty("word").GetString()));
        Assert.Equal((403, Capabilities.NoRoleGrants(Bit.ManageStanding).Word), (banned.Status, banned.Structured!.Value.GetProperty("word").GetString()));
    }
}
