using System.Net;
using System.Text.Json;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Tools;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Space;

/// <summary>Quieting a Tangent: what is new in it stops counting on the reader's door — its news and its count in the rail —
/// while a reply to them there and a Topic they follow there still reach them; letting it count again brings it back; and
/// the Tangent's page says whether it is quiet, with its form.</summary>
public sealed class QuietTests
{
    private static async Task<JsonElement> Door(Browser session) => (await Send(session.Client, HttpMethod.Get, "/", json: true)).Json;

    private static bool Counted(JsonElement door, string slug)
        => door.GetProperty("news").GetProperty("perTangent").EnumerateArray().Any(tangent => tangent.GetProperty("slug").GetString() == slug)
           || door.GetProperty("tangents").EnumerateArray().Any(tangent => tangent.GetProperty("slug").GetString() == slug && tangent.GetProperty("new").GetInt32() > 0);

    [Fact]
    public async Task A_quiet_tangent_stops_counting_on_the_door_while_what_is_for_the_reader_still_reaches_them_until_it_counts_again()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var mine = await Started(Ada, "Lanterns", "Who lights theirs first?");
        using var ada = await SignedIn(tangentSpace, Ada);

        // The Tangent's page offers quieting it, and the form says so.
        var page = (await Send(ada.Client, HttpMethod.Get, "/t/" + Workshop, json: true)).Json;
        Assert.False(page.GetProperty("quiet").GetBoolean());
        Assert.Equal("true", Valued(page.GetProperty("quieting"), "quiet"));
        var quieted = await Form(ada, "/t/" + Workshop, "/t/" + Workshop + "/quiet", new() { ["quiet"] = "true" });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/t/" + Workshop), (quieted.Status, quieted.Location));
        page = (await Send(ada.Client, HttpMethod.Get, "/t/" + Workshop, json: true)).Json;
        Assert.True(page.GetProperty("quiet").GetBoolean());
        Assert.Contains(">Stop quieting</button>", (await Send(ada.Client, HttpMethod.Get, "/t/" + Workshop)).Body);

        // Bea starts a Topic there: nothing of it counts on Ada's door.
        var beas = await Started(Bea, "Kites", "Where do you fly yours?");
        Assert.False(Counted(await Door(ada), Workshop));

        // A reply to Ada there still reaches her, and so does the Topic she follows there.
        await Replied(mine, Bea, "I do, at six.");
        var door = await Door(ada);
        Assert.Equal(1, door.GetProperty("news").GetProperty("replies").GetProperty("count").GetInt32());
        Assert.False(Counted(door, Workshop));
        await Replied(beas, Ada, "On the hill.");
        await Replied(beas, Mo, "On the beach.");
        door = await Door(ada);
        Assert.Equal(beas.Id, Assert.Single(door.GetProperty("news").GetProperty("following").EnumerateArray()).GetProperty("topicId").GetString());

        // Through the tool, it counts again.
        using (var tools = await ToolClient.Connect(tangentSpace, ada))
        {
            var again = await tools.Call("quiet_tangent", new { tangent = Workshop, quiet = false });
            Assert.False(again.Failed, again.Text);
            Assert.False(again.Structured!.Value.GetProperty("quiet").GetBoolean());
        }
        Assert.True(Counted(await Door(ada), Workshop));
    }
}
