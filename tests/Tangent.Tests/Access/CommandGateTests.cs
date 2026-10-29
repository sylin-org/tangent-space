using Microsoft.Extensions.DependencyInjection;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;
using Xunit;

namespace Tangent.Access;

/// <summary>The writer's commands ask for the verb they need and refuse with the gate word, writing nothing.</summary>
public sealed class CommandGateTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string Workshop = "the-workshop";

    [Fact]
    public async Task A_member_starting_a_topic_where_everyone_is_denied_topic_is_refused_with_the_gate_word_and_writes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Member], ct: Cancellation);
        var everyone = Overwrite.ForAudience(Workshop, Audience.Public);
        everyone.Deny = [nameof(Bit.Topic)];
        Assert.IsType<Outcome<Overwrite>.Done>(await writer.Send(new Store<Overwrite>(everyone), Cancellation));
        var announced = 0;
        writer.Committed += _ => announced++;

        var refused = await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Not here", "Words.", "leo"), Cancellation);

        Assert.Equal(Capabilities.Denied(Overwrite.Everyone, Bit.Topic), Assert.IsType<Outcome<Post>.Refused>(refused).Reason);
        Assert.Equal((0, 0, 0), (await Post.Count.Exact(Cancellation), await Event.Count.Exact(Cancellation), announced));
    }

    [Fact]
    public async Task A_moderator_replying_on_a_locked_topic_is_refused_with_locked_and_on_a_removed_one_with_removed()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "mo", [Roles.Moderator], ct: Cancellation);
        var at = DateTimeOffset.UtcNow;
        var locked = Post.Root(Tids.Mint(), Workshop, "Locked", "locked", PostKind.Topic, "No more.", "leo", at);
        locked.Locked = true;
        var removed = Post.Root(Tids.Mint(), Workshop, "Removed", "removed", PostKind.Topic, "Gone.", "leo", at);
        removed.Removed = true;
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(locked), Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(removed), Cancellation));

        var onLocked = await writer.Send(new Reply(Tids.Mint(), locked.Id, "Still?", "mo"), Cancellation);
        var onRemoved = await writer.Send(new Reply(Tids.Mint(), removed.Id, "Still?", "mo"), Cancellation);

        Assert.Equal(Capabilities.Locked, Assert.IsType<Outcome<Post>.Refused>(onLocked).Reason);
        Assert.Equal(Capabilities.Removed, Assert.IsType<Outcome<Post>.Refused>(onRemoved).Reason);
        Assert.Equal(2, await Post.Count.Exact(Cancellation));
        Assert.Equal(0, await Event.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task Creating_a_tangent_needs_manage_space_which_an_administrator_holds_and_a_member_does_not()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ada", [Roles.Member], ct: Cancellation);

        var byMember = await writer.Send(new CreateTangent("articles", "Articles", "", PostKind.Article, 1, Audience.Public, "ada"), Cancellation);
        var byAdministrator = await writer.Send(new CreateTangent("articles", "Articles", "", PostKind.Article, 1, Audience.Public, "leo"), Cancellation);

        Assert.Equal(Capabilities.NoRoleGrants(Bit.ManageSpace), Assert.IsType<Outcome<Tangents.Tangent>.Refused>(byMember).Reason);
        Assert.Equal("articles", Assert.IsType<Outcome<Tangents.Tangent>.Done>(byAdministrator).Result.Id);
        Assert.Equal(1, await Tangents.Tangent.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task A_visitor_a_timed_out_and_an_applied_participant_are_refused_with_their_standings_word()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "tim", [Roles.Member], Standing.TimedOut, Cancellation);
        await Given.AParticipant(tangentSpace, "app", [Roles.Member], Standing.Applied, Cancellation);
        await Given.AParticipant(tangentSpace, "ban", [Roles.Member], Standing.Banned, Cancellation);

        var visitor = await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Hello", "Anyone?", "nobody"), Cancellation);
        var timedOut = await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Hello", "Anyone?", "tim"), Cancellation);
        var applied = await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Hello", "Anyone?", "app"), Cancellation);
        var banned = await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Hello", "Anyone?", "ban"), Cancellation);

        Assert.Equal(Capabilities.SignIn, Assert.IsType<Outcome<Post>.Refused>(visitor).Reason);
        Assert.Equal(Capabilities.TimedOut, Assert.IsType<Outcome<Post>.Refused>(timedOut).Reason);
        Assert.Equal(Capabilities.Apply, Assert.IsType<Outcome<Post>.Refused>(applied).Reason);
        Assert.Equal(Capabilities.Banned, Assert.IsType<Outcome<Post>.Refused>(banned).Reason);
        Assert.Equal(0, await Post.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task A_topic_in_a_tangent_that_does_not_exist_is_refused_and_one_in_a_tangent_the_member_cannot_see_is_refused_naming_everyone()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.ATangent(tangentSpace, "off-the-record", Audience.Private, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Member], ct: Cancellation);

        var nowhere = await writer.Send(new StartTopic(Tids.Mint(), "nowhere", "Hello", "Anyone?", "leo"), Cancellation);
        var unseen = await writer.Send(new StartTopic(Tids.Mint(), "off-the-record", "Hello", "Anyone?", "leo"), Cancellation);

        Assert.Equal(StartTopic.NoTangent, Assert.IsType<Outcome<Post>.Refused>(nowhere).Reason);
        Assert.Equal(Capabilities.Denied(Overwrite.Everyone, Bit.View), Assert.IsType<Outcome<Post>.Refused>(unseen).Reason);
        Assert.Equal(0, await Post.Count.Exact(Cancellation));
    }
}
