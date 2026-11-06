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

/// <summary>The commands ask for the verb they need and refuse with the gate word, writing nothing; what
/// the actor may not View does not exist for them, and the answer is the one a missing resource gets.</summary>
public sealed class CommandGateTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string Workshop = "the-workshop";

    [Fact]
    public async Task A_member_starting_a_topic_where_everyone_is_denied_topic_is_refused_with_the_gate_word_and_writes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Member], ct: Cancellation);
        await tangentSpace.Services.GetRequiredService<RoleCollection>().Overwrite(Overwrites.Resource(Workshop), OverwriteSubjectKind.Role,
            RoleTokens.Everyone, [Bits.Token(Bit.View)], [Bits.Token(Bit.Topic)], ct: Cancellation);

        var refused = await new StartTopic(Tids.Mint(), Workshop, "Not here", "Words.", "leo").Send(Cancellation);

        Assert.Equal(Capabilities.Denied(RoleTokens.Everyone, Bit.Topic), Assert.IsType<Outcome<Post>.Refused>(refused).Reason);
        Assert.Equal((0, 0), (await Post.Count.Exact(Cancellation), await Event.Count.Exact(Cancellation)));
    }

    [Fact]
    public async Task A_moderator_replying_on_a_topic_whose_root_is_removed_is_refused_as_closed()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "mo", [Roles.Moderator], ct: Cancellation);
        var removed = Post.Root(Tids.Mint(), Workshop, "Removed", "removed", PostKind.Topic, "Gone.", "leo", DateTimeOffset.UtcNow);
        removed.Removed = true;
        Assert.IsType<Outcome<Post>.Done>(await new Store<Post>(removed).Send(Cancellation));

        var onRemoved = await new Reply(Tids.Mint(), removed.Id, "Still?", "mo").Send(Cancellation);

        Assert.Equal(Capabilities.Closed, Assert.IsType<Outcome<Post>.Refused>(onRemoved).Reason);
        Assert.Equal(1, await Post.Count.Exact(Cancellation));
        Assert.Equal(0, await Event.Count.Exact(Cancellation));
    }

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
    public async Task A_visitor_a_timed_out_and_an_applied_participant_are_refused_with_their_standings_word_and_a_banned_one_finds_no_tangent()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "tim", [Roles.Member], Standing.TimedOut, Cancellation);
        await Given.AParticipant(tangentSpace, "app", [Roles.Member], Standing.Applied, Cancellation);
        await Given.AParticipant(tangentSpace, "ban", [Roles.Member], Standing.Banned, Cancellation);

        var visitor = await new StartTopic(Tids.Mint(), Workshop, "Hello", "Anyone?", "nobody").Send(Cancellation);
        var timedOut = await new StartTopic(Tids.Mint(), Workshop, "Hello", "Anyone?", "tim").Send(Cancellation);
        var applied = await new StartTopic(Tids.Mint(), Workshop, "Hello", "Anyone?", "app").Send(Cancellation);
        var banned = await new StartTopic(Tids.Mint(), Workshop, "Hello", "Anyone?", "ban").Send(Cancellation);

        Assert.Equal(Capabilities.SignIn, Assert.IsType<Outcome<Post>.Refused>(visitor).Reason);
        Assert.Equal(Capabilities.TimedOut, Assert.IsType<Outcome<Post>.Refused>(timedOut).Reason);
        Assert.Equal(Capabilities.Apply, Assert.IsType<Outcome<Post>.Refused>(applied).Reason);
        Assert.Equal(StartTopic.NoTangent, Assert.IsType<Outcome<Post>.Refused>(banned).Reason);
        Assert.Equal(0, await Post.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task A_tangent_that_does_not_exist_and_one_the_member_may_not_view_get_the_same_answer()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Given.ATangent(tangentSpace, "off-the-record", Audience.Private, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Member], ct: Cancellation);

        var nowhere = await new StartTopic(Tids.Mint(), "nowhere", "Hello", "Anyone?", "leo").Send(Cancellation);
        var unseen = await new StartTopic(Tids.Mint(), "off-the-record", "Hello", "Anyone?", "leo").Send(Cancellation);

        Assert.Equal(StartTopic.NoTangent, Assert.IsType<Outcome<Post>.Refused>(nowhere).Reason);
        Assert.Equal(StartTopic.NoTangent, Assert.IsType<Outcome<Post>.Refused>(unseen).Reason);
        Assert.Equal(0, await Post.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task A_banned_participant_and_a_visitor_on_a_private_place_get_one_answer_for_an_existing_and_a_missing_resource()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, audience: Audience.Private, ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ban", [Roles.Member], Standing.Banned, Cancellation);
        var topicId = Tids.Mint();
        Assert.IsType<Outcome<Post>.Done>(await new StartTopic(topicId, Workshop, "Here", "Among members.", "leo").Send(Cancellation));
        var missingTopic = Tids.Mint();

        foreach (var actor in new[] { "ban", "nobody" })
        {
            var existingTopic = await new Reply(Tids.Mint(), topicId, "Still?", actor).Send(Cancellation);
            var noTopic = await new Reply(Tids.Mint(), missingTopic, "Still?", actor).Send(Cancellation);
            var existingTangent = await new StartTopic(Tids.Mint(), Workshop, "Hello", "Anyone?", actor).Send(Cancellation);
            var noTangent = await new StartTopic(Tids.Mint(), "nowhere", "Hello", "Anyone?", actor).Send(Cancellation);
            var place = await new CreateTangent(Workshop, "Other", "", PostKind.Topic, 1, Audience.Public, actor).Send(Cancellation);

            Assert.Equal(Reply.NoTopic, Assert.IsType<Outcome<Post>.Refused>(existingTopic).Reason);
            Assert.Equal(Reply.NoTopic, Assert.IsType<Outcome<Post>.Refused>(noTopic).Reason);
            Assert.Equal(StartTopic.NoTangent, Assert.IsType<Outcome<Post>.Refused>(existingTangent).Reason);
            Assert.Equal(StartTopic.NoTangent, Assert.IsType<Outcome<Post>.Refused>(noTangent).Reason);
            Assert.Equal(CreateTangent.NoPlace, Assert.IsType<Outcome<Tangents.Tangent>.Refused>(place).Reason);
        }

        Assert.Equal(1, await Post.Count.Exact(Cancellation));
        Assert.Equal(1, await Tangents.Tangent.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task A_replay_of_a_committed_command_by_an_actor_who_has_since_lost_view_answers_absent_not_done()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var leo = await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        var at = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var createTangent = new CreateTangent(Workshop, "The workshop", "", PostKind.Topic, 1, Audience.Public, "leo", at);
        var startTopic = new StartTopic(Tids.Mint(), Workshop, "Here", "Words.", "leo", At: at);
        var reply = new Reply(Tids.Mint(), startTopic.Id, "More.", "leo", At: at);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await createTangent.Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await startTopic.Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await reply.Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await reply.Send(Cancellation));
        leo.Standing = Standing.Banned;
        Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(leo).Send(Cancellation));

        var tangentAgain = await createTangent.Send(Cancellation);
        var topicAgain = await startTopic.Send(Cancellation);
        var replyAgain = await reply.Send(Cancellation);

        Assert.Equal(CreateTangent.NoPlace, Assert.IsType<Outcome<Tangents.Tangent>.Refused>(tangentAgain).Reason);
        Assert.Equal(StartTopic.NoTangent, Assert.IsType<Outcome<Post>.Refused>(topicAgain).Reason);
        Assert.Equal(Reply.NoTopic, Assert.IsType<Outcome<Post>.Refused>(replyAgain).Reason);
        Assert.Equal(3, await Event.Count.Exact(Cancellation));
    }
}
