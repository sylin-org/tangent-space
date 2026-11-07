using System.Net;
using Koan.Data.Core.Commands;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Participants;

/// <summary>A participant chooses their own kind on their own profile: choosing agent marks them one everywhere — their
/// profile's JSON and badge, and their own account chip — taking it back makes them a person again, a profile read after
/// choosing keeps their choice, and an account whose own bot label makes it an agent sees the person choice locked and is
/// refused taking it back, by a page or a tool.</summary>
public sealed class KindTests
{
    private static readonly string AdaHandle = Persona.Handle(Ada);

    [Fact]
    public async Task A_participant_marks_themselves_an_agent_on_their_profile_and_takes_it_back_and_a_profile_read_keeps_their_choice()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var profile = "/p/" + AdaHandle;

        var own = await Send(ada.Client, HttpMethod.Get, profile, json: true);
        Assert.Equal("person", Valued(own.Json.GetProperty("shownAs"), PeopleController.KindField));
        Assert.Contains("Shown here as", (await Send(ada.Client, HttpMethod.Get, profile)).Body);
        using var bea = await SignedIn(tangentSpace, Bea);
        Assert.False((await Send(bea.Client, HttpMethod.Get, profile, json: true)).Json.TryGetProperty("shownAs", out _));

        var chosen = await Form(ada, profile, PeopleController.KindPath, new Dictionary<string, string> { [PeopleController.KindField] = "agent" });

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + profile), (chosen.Status, chosen.Location));
        var read = await Send(ada.Client, HttpMethod.Get, profile, json: true);
        Assert.Equal(("agent", "agent"), (read.Json.GetProperty("kind").GetString(), read.Json.GetProperty("who").GetProperty("kind").GetString()));
        var html = await Send(bea.Client, HttpMethod.Get, profile);
        Assert.Contains("<span class=\"badge is-agent\">Agent</span>", html.Body);
        var door = (await Send(ada.Client, HttpMethod.Get, "/")).Body;
        var chip = door[door.IndexOf("class=\"account-button\"", StringComparison.Ordinal)..];
        Assert.Contains("is-agent", chip[..chip.IndexOf("</summary>", StringComparison.Ordinal)]);

        // A profile read after the choice, of an account with no bot label, keeps it.
        var participant = (await Participant.Get(Ada, Cancellation))!;
        Assert.IsType<Outcome<Participant>.Done>(await new CaptureProfile(Ada, new ProfileAnswer(participant.Did, "Ada", null, null), DateTimeOffset.UtcNow).Send(Cancellation));
        Assert.Equal((ParticipantKind.Agent, KindSource.OwnChoice), ((await Participant.Get(Ada, Cancellation))!.Kind, (await Participant.Get(Ada, Cancellation))!.KindSource));

        // Taken back as JSON, the same write a tool sends.
        var back = await Json(ada, profile, PeopleController.KindPath, new { kind = "person" });
        Assert.Equal((HttpStatusCode.OK, "person", false), (back.Status, back.Json.GetProperty("kind").GetString(), back.Json.GetProperty("labelled").GetBoolean()));
        Assert.Equal("person", (await Send(ada.Client, HttpMethod.Get, profile, json: true)).Json.GetProperty("kind").GetString());
        var neither = await Json(ada, profile, PeopleController.KindPath, new { kind = "robot" });
        Assert.Equal((HttpStatusCode.BadRequest, "invalid"), (neither.Status, neither.Word));
    }

    [Fact]
    public async Task An_account_whose_own_label_makes_it_an_agent_sees_the_person_choice_locked_and_is_refused_taking_it_back()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var participant = (await Participant.Get(Ada, Cancellation))!;
        // Every row the label made an agent before anyone could choose stands so: an agent by its label.
        participant.Kind = ParticipantKind.Agent;
        Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(participant).Send(Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);
        var profile = "/p/" + AdaHandle;

        var html = (await Send(ada.Client, HttpMethod.Get, profile)).Body;
        Assert.Contains("Your Bluesky account labels itself a bot", html);
        Assert.Contains("name=\"kind\" value=\"person\" aria-pressed=\"false\" disabled", html);

        var refused = await Json(ada, profile, PeopleController.KindPath, new { kind = "person" });
        Assert.Equal((HttpStatusCode.Conflict, "labelled"), (refused.Status, refused.Word));
        Assert.Equal(ParticipantKind.Agent, (await Participant.Get(Ada, Cancellation))!.Kind);

        using var agent = await Tools.ToolClient.Connect(tangentSpace, ada);
        var called = await agent.Call("choose_kind", new { kind = "person" });
        Assert.Equal((409, "labelled"), (called.Status, called.Meta.GetProperty("problem").GetProperty("word").GetString()));
        var again = await agent.Call("choose_kind", new { kind = "agent" });
        Assert.False(again.Failed, again.Text);
        Assert.True(again.Structured!.Value.GetProperty("labelled").GetBoolean());
    }
}
