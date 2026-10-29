using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Writer;
using Xunit;

namespace Tangent.Tangents;

/// <summary>A Tangent set up by command: its row, its audience as the everyone overwrite, and the log row that says so.</summary>
public sealed class TangentTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_tangent_created_by_command_exists_with_its_slug_as_key_and_appends_tangent_changed_carrying_its_audience()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        var announced = new List<Committed>();
        writer.Committed += announced.Add;
        var at = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        var outcome = await writer.Send(new CreateTangent("the-workshop", "The workshop", "Where things get made", PostKind.Topic, 2, Audience.Private, "leo", at), Cancellation);

        var tangent = Assert.IsType<Outcome<Tangent>.Done>(outcome).Result;
        Assert.Equal("the-workshop", tangent.Id);
        var stored = await Tangent.Get("the-workshop", Cancellation);
        Assert.NotNull(stored);
        Assert.Equal(("The workshop", "Where things get made", PostKind.Topic, 2), (stored.Name, stored.Description, stored.DefaultKind, stored.Order));

        var everyone = await Overwrite.Get(Overwrite.Key("the-workshop", Overwrite.Everyone), Cancellation);
        Assert.NotNull(everyone);
        Assert.Equal(Audience.Private, everyone.AudienceStated());
        Assert.Contains(Overwrite.View, everyone.Deny);

        var logged = Assert.Single(await Event.All(Cancellation));
        Assert.Equal((1L, EventKind.TangentChanged, "the-workshop", "leo", Audience.Private, at), (logged.Seq, logged.Kind, logged.TangentId, logged.ActorId, logged.Audience, logged.At));
        Assert.Equal(Event.Key(1), logged.Id);
        var commit = Assert.Single(announced);
        Assert.Equal(1L, commit.Sequences[Event.Log]);
    }

    [Fact]
    public async Task The_same_slug_with_the_same_fields_answers_with_the_existing_tangent_and_different_fields_are_a_conflict()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);

        var first = Assert.IsType<Outcome<Tangent>.Done>(await writer.Send(new CreateTangent("articles", "Articles", "", PostKind.Article, 1, Audience.Public, "leo"), Cancellation)).Result;
        var again = await writer.Send(new CreateTangent("articles", "Articles", "", PostKind.Article, 1, Audience.Public, "leo"), Cancellation);
        var differs = await writer.Send(new CreateTangent("articles", "Articles", "", PostKind.Article, 1, Audience.Private, "leo"), Cancellation);

        Assert.Equal(first.Id, Assert.IsType<Outcome<Tangent>.Done>(again).Result.Id);
        Assert.Contains("'articles'", Assert.IsType<Outcome<Tangent>.Conflict>(differs).Reason);
        Assert.Equal(1, await Event.Count.Exact(Cancellation));
        Assert.Equal(1, await Tangent.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task A_slug_that_is_not_a_slug_is_refused_and_nothing_is_written()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);

        var refused = await writer.Send(new CreateTangent("The Workshop", "The workshop", "", PostKind.Topic, 1, Audience.Public, "leo"), Cancellation);

        Assert.Equal(Tangent.NeedsSlug, Assert.IsType<Outcome<Tangent>.Refused>(refused).Reason);
        Assert.Equal(0, await Tangent.Count.Exact(Cancellation));
        Assert.Equal(0, await Event.Count.Exact(Cancellation));
    }
}
