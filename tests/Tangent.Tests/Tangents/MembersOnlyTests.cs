using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Tangents;

/// <summary>A members-only Tangent whose joining says ask: someone outside it finds it listed and asks to join; whoever
/// manages it sees the ask on their door and on its people tab and lets them in; the asker's door tells them, they read
/// it, and they leave it with one action.</summary>
public sealed class MembersOnlyTests
{
    private const string Leo = "did:plc:leo4members";

    [Fact]
    public async Task Someone_outside_asks_to_join_is_let_in_from_the_people_tab_is_told_on_their_door_and_leaves()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Member, Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        var people = "/t/" + Studio + "/settings/people";

        // Leo opens the private studio to asks; Ada finds it listed with the way in, and still cannot read it.
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, people, "/t/" + Studio + "/settings/joining", new { joining = "ask" })).Status);
        var listed = Listed((await Send(ada.Client, HttpMethod.Get, "/t", json: true)).Json, Studio);
        Assert.Equal("ask", listed.GetProperty("joining").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ada.Client, HttpMethod.Get, "/t/" + Studio)).Status);

        // She asks; Leo's door and the people tab show her asking.
        var asked = await Json(ada, "/t", new Uri(listed.GetProperty("join").GetProperty("action").GetString()!).AbsolutePath, new { });
        Assert.Equal((HttpStatusCode.OK, "asked"), (asked.Status, asked.Json.GetProperty("state").GetString()));
        Assert.Equal(1, (await Send(leo.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("asks")[0].GetProperty("count").GetInt32());
        var page = await Send(leo.Client, HttpMethod.Get, people, json: true);
        Assert.Equal([Ada], page.Json.GetProperty("asking").EnumerateArray().Select(person => person.GetProperty("id").GetString()));

        // Leo lets her in: she is listed among the people, reads the studio, and her door tells her.
        var letIn = await Json(leo, people, people, new { person = Ada, choice = "in" });
        Assert.Equal(HttpStatusCode.OK, letIn.Status);
        Assert.Empty(letIn.Json.GetProperty("asking").EnumerateArray());
        Assert.Equal([Ada], letIn.Json.GetProperty("people").EnumerateArray().Select(person => person.GetProperty("id").GetString()));
        var studio = await Send(ada.Client, HttpMethod.Get, "/t/" + Studio, json: true);
        Assert.Equal(HttpStatusCode.OK, studio.Status);
        var let = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("let")[0];
        Assert.True(let.GetProperty("given").GetBoolean());

        // She leaves with the page's form, and the studio is closed to her again.
        var left = await Json(ada, "/t/" + Studio, new Uri(studio.Json.GetProperty("leave").GetProperty("action").GetString()!).AbsolutePath, new { });
        Assert.Equal((HttpStatusCode.OK, "out"), (left.Status, left.Json.GetProperty("state").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ada.Client, HttpMethod.Get, "/t/" + Studio)).Status);

        // Opened to anyone, then Leo keeps her out: she cannot join again, and the studio is no longer listed to her.
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, people, "/t/" + Studio + "/settings/joining", new { joining = "join" })).Status);
        var keptOut = await Json(leo, people, people, new { person = Ada, choice = "out" });
        Assert.Equal("kept-out", keptOut.Json.GetProperty("people").EnumerateArray().Single().GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Json(ada, "/t", "/t/" + Studio + "/join", new { })).Status);
        Assert.DoesNotContain(Studio, (await Send(ada.Client, HttpMethod.Get, "/t", json: true)).Json.GetProperty("tangents").EnumerateArray()
            .Select(tangent => tangent.GetProperty("slug").GetString()));

        // Taking out is done only where it keeps someone out: never oneself, and never in a public Tangent, read signed out.
        var themselves = await Json(leo, people, people, new { person = Leo, choice = "out" });
        Assert.Equal((HttpStatusCode.Forbidden, Inside.Themselves.Word), (themselves.Status, themselves.Word));
        var workshop = "/t/" + Workshop + "/settings/people";
        var everyone = await Json(leo, workshop, workshop, new { person = Ada, choice = "out" });
        Assert.Equal((HttpStatusCode.Conflict, Inside.Public.Word), (everyone.Status, everyone.Word));
        Assert.Equal(HttpStatusCode.OK, (await Send(ada.Client, HttpMethod.Get, "/t/" + Workshop)).Status);
    }

    [Fact]
    public async Task A_banned_participant_leaves_nothing_and_finds_one_answer_for_a_tangent_they_joined_and_a_missing_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Given.ATangent(tangentSpace, Studio, Audience.Private, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ban", [Roles.Member], Standing.Banned, Cancellation);
        await Inside.LetIn(RoleSettings.Collection(), Studio, "ban", Cancellation);

        var joined = Assert.IsType<Outcome<Tangent>.Refused>(await new LeaveTangent(Studio, "ban").Send(Cancellation));
        var missing = Assert.IsType<Outcome<Tangent>.Refused>(await new LeaveTangent("nowhere", "ban").Send(Cancellation));
        var join = Assert.IsType<Outcome<Joined>.Refused>(await new JoinTangent(Studio, "ban").Send(Cancellation));

        Assert.Equal(joined.Reason, missing.Reason);
        Assert.Equal(missing.Reason, join.Reason);
        Assert.True(Inside.In(await Inside.Own(Studio, "ban", Cancellation)));
    }

    private static JsonElement Listed(JsonElement page, string slug)
        => page.GetProperty("tangents").EnumerateArray().Single(tangent => tangent.GetProperty("slug").GetString() == slug);
}
