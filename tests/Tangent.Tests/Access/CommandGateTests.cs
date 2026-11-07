using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Xunit;

namespace Tangent.Access;

/// <summary>A command asks for the verb it needs and refuses with the gate word, writing nothing; what the actor may not View
/// does not exist for them, and the answer is the one a missing resource gets. A Post's writes are judged at the door alone
/// (<see cref="DoorwayTests"/>).</summary>
public sealed class CommandGateTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string Workshop = "the-workshop";

    [Fact]
    public async Task Creating_a_tangent_needs_manage_space_which_an_administrator_holds_and_a_member_does_not()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ada", [Roles.Member], ct: Cancellation);

        var byMember = await new CreateTangent("articles", "Articles", "", PostKind.Article, 1, Audience.Public, "ada").Send(Cancellation);
        var byAdministrator = await new CreateTangent("articles", "Articles", "", PostKind.Article, 1, Audience.Public, "leo").Send(Cancellation);

        Assert.Equal(Capabilities.NoRoleGrants(Bit.ManageSpace), Assert.IsType<Outcome<Tangents.Tangent>.Refused>(byMember).Reason);
        Assert.Equal("articles", Assert.IsType<Outcome<Tangents.Tangent>.Done>(byAdministrator).Result.Id);
        Assert.Equal(1, await Tangents.Tangent.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task A_replay_of_a_committed_command_by_an_actor_who_has_since_lost_view_answers_absent_not_done()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var leo = await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        var at = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var createTangent = new CreateTangent(Workshop, "The workshop", "", PostKind.Topic, 1, Audience.Public, "leo", at);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await createTangent.Send(Cancellation));
        leo.Standing = Standing.Banned;
        Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(leo).Send(Cancellation));

        var tangentAgain = await createTangent.Send(Cancellation);

        Assert.Equal(CreateTangent.NoPlace, Assert.IsType<Outcome<Tangents.Tangent>.Refused>(tangentAgain).Reason);
        Assert.Equal(1, await Event.Count.Exact(Cancellation));
    }
}
