using System.Net;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Tangents;

/// <summary>A members-only Tangent whose joining says ask: someone outside it finds it listed, and at its own address its
/// card, which carries no picture of its own they could not open, and asks to join; whoever manages it sees the ask on their
/// door and on its people tab and lets them in; the asker's door tells them, they read it, and they leave it with one action.</summary>
public sealed class MembersOnlyTests
{
    private static readonly string Leo = Persona.Id("leo4members");

    [Fact]
    public async Task Someone_outside_asks_to_join_is_let_in_from_the_people_tab_is_told_on_their_door_and_leaves()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Member, Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        var people = "/t/" + Studio + "/settings/people";

        // Leo opens the private studio to asks; Ada's door leads her to all the Tangents, however few the place holds,
        // where she finds it listed with the way in, and still cannot read it. A visitor's door, which can join nothing,
        // has no such link.
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, people, HttpMethod.Patch, "/t/" + Studio, new { joining = "ask" })).Status);
        var door = await Send(ada.Client, HttpMethod.Get, "/", json: true);
        Assert.Equal((false, BaseUrl + TangentsController.Route), (door.Json.GetProperty("moreTangents").GetBoolean(), door.Json.GetProperty("allTangents").GetString()));
        Assert.Contains($"<a class=\"rail-new\" href=\"{BaseUrl + TangentsController.Route}\">All Tangents</a>", (await Send(ada.Client, HttpMethod.Get, "/")).Body);
        using (var visitor = Agent(tangentSpace))
            Assert.False((await Send(visitor, HttpMethod.Get, "/", json: true)).Json.TryGetProperty("allTangents", out _));
        var listed = Listed((await Send(ada.Client, HttpMethod.Get, "/t", json: true)).Json, Studio);
        Assert.Equal("ask", listed.GetProperty("joining").GetString());

        // Leo brings the studio a picture of its own, which reaches only its members, so the card Ada reads carries none.
        var picture = await Media.Keep(ArticleMediaTests.Jpeg(), Cancellation);
        Assert.IsType<Outcome<MediaUse>.Done>(await new BringMedia(picture.Id, Leo).Send(Cancellation));
        Assert.IsType<Outcome<Tangent>.Done>(await new ChangeTangentRow(Studio, Leo, row => row.Image = Media.Reference(picture.Id)).Send(Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ada.Client, HttpMethod.Get, Media.Reference(picture.Id))).Status);
        listed = Listed((await Send(ada.Client, HttpMethod.Get, "/t", json: true)).Json, Studio);
        Assert.Null(Image(listed));

        // A member's link to the studio opens its card for her, the same card, with nothing it holds; under it, nothing.
        var card = await Send(ada.Client, HttpMethod.Get, "/t/" + Studio, json: true);
        Assert.Equal(HttpStatusCode.OK, card.Status);
        Assert.Equal(listed.GetRawText(), card.Json.GetProperty("tangent").GetRawText());
        Assert.False(card.Json.TryGetProperty("topics", out _));
        var drawn = await Send(ada.Client, HttpMethod.Get, "/t/" + Studio);
        Assert.Contains("Ask to join", drawn.Body);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ada.Client, HttpMethod.Get, "/t/" + Studio + "/settings")).Status);

        // She asks from it; it says her ask waits, with the way to withdraw it, and Leo's door and the people tab show her asking.
        var asked = await Json(ada, "/t/" + Studio, new Uri(card.Json.GetProperty("tangent").GetProperty("join").GetProperty("action").GetString()!).AbsolutePath, new { });
        Assert.Equal((HttpStatusCode.OK, "asked"), (asked.Status, asked.Json.GetProperty("state").GetString()));
        var waiting = (await Send(ada.Client, HttpMethod.Get, "/t/" + Studio, json: true)).Json.GetProperty("tangent");
        Assert.Equal("asked", waiting.GetProperty("joining").GetString());
        Assert.EndsWith("/t/" + Studio + "/" + JoinController.LeaveSegment, waiting.GetProperty("withdraw").GetProperty("action").GetString());
        Assert.Equal(1, (await Send(leo.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("asks")[0].GetProperty("count").GetInt32());
        var page = await Send(leo.Client, HttpMethod.Get, people, json: true);
        Assert.Equal([Ada], page.Json.GetProperty("asking").EnumerateArray().Select(row => row.GetProperty("person").GetProperty("id").GetString()));

        // Leo lets her in: she is listed among the people, reads the studio, and her door tells her.
        var letIn = await Json(leo, people, people, new { person = Ada, choice = "in" });
        Assert.Equal(HttpStatusCode.OK, letIn.Status);
        Assert.Empty(letIn.Json.GetProperty("asking").EnumerateArray());
        Assert.Equal([Ada], letIn.Json.GetProperty("people").EnumerateArray().Select(row => row.GetProperty("person").GetProperty("id").GetString()));
        var studio = await Send(ada.Client, HttpMethod.Get, "/t/" + Studio, json: true);
        Assert.Equal(HttpStatusCode.OK, studio.Status);
        Assert.Equal(BaseUrl + Media.Reference(picture.Id), Image(Listed((await Send(ada.Client, HttpMethod.Get, "/t", json: true)).Json, Studio)));
        Assert.Equal(HttpStatusCode.OK, (await Send(ada.Client, HttpMethod.Get, Media.Reference(picture.Id))).Status);
        var let = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("let")[0];
        Assert.True(let.GetProperty("given").GetBoolean());

        // She leaves with the page's form, and the studio is closed to her again.
        var left = await Json(ada, "/t/" + Studio, new Uri(studio.Json.GetProperty("leave").GetProperty("action").GetString()!).AbsolutePath, new { });
        Assert.Equal((HttpStatusCode.OK, "out"), (left.Status, left.Json.GetProperty("state").GetString()));
        Assert.Equal("ask", (await Send(ada.Client, HttpMethod.Get, "/t/" + Studio, json: true)).Json.GetProperty("tangent").GetProperty("joining").GetString());

        // Closed, the studio is absent to her at its address.
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, people, HttpMethod.Patch, "/t/" + Studio, new { joining = "closed" })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ada.Client, HttpMethod.Get, "/t/" + Studio)).Status);

        // Opened to anyone, then Leo keeps her out: she cannot join again, and the studio is no longer listed to her.
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, people, HttpMethod.Patch, "/t/" + Studio, new { joining = "join" })).Status);
        var keptOut = await Json(leo, people, people, new { person = Ada, choice = "out" });
        Assert.Equal("kept-out", keptOut.Json.GetProperty("people").EnumerateArray().Single().GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Json(ada, "/t", "/t/" + Studio + "/join", new { })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ada.Client, HttpMethod.Get, "/t/" + Studio)).Status);
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

    /// <summary>Whoever manages several members-only Tangents reads on their door how many ask in each, however many newer
    /// asks wait in another: a Tangent past a page of asks says at least that many, and one with a single older ask still
    /// says it.</summary>
    [Fact]
    public async Task The_door_counts_the_asks_of_each_tangent_its_reader_manages_saying_at_least_past_a_page()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Member, Roles.Administrator], ct: Cancellation);
        const string kiln = "kiln";
        await Given.ATangent(tangentSpace, kiln, Audience.Private, ct: Cancellation);
        foreach (var slug in new[] { Studio, kiln })
            Assert.IsType<Outcome<Tangent>.Done>(await new ChangeTangentRow(slug, Leo, row => row.Joining = Joining.Ask).Send(Cancellation));
        var at = DateTimeOffset.UtcNow.AddHours(-2);
        await new Ask { Id = Ask.Key(kiln, "early"), TangentId = kiln, PersonId = "early", AskedAt = at }.Save(Cancellation);
        for (var index = 0; index <= Ask.ReadSize; index++)
            await new Ask { Id = Ask.Key(Studio, "asker" + index), TangentId = Studio, PersonId = "asker" + index, AskedAt = at.AddMinutes(index + 1) }.Save(Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);

        var asks = (await Send(leo.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("asks").EnumerateArray()
            .ToDictionary(row => row.GetProperty("address").GetString()!, row => (row.GetProperty("count").GetInt32(), row.GetProperty("atLeast").GetBoolean()));
        Assert.Equal((Ask.ReadSize, true), asks[TangentPeopleController.PeopleAddress(BaseUrl, Studio)]);
        Assert.Equal((1, false), asks[TangentPeopleController.PeopleAddress(BaseUrl, kiln)]);
        Assert.Contains($"At least {Ask.ReadSize} people ask to join", (await Send(leo.Client, HttpMethod.Get, "/")).Body);
    }

    private static JsonElement Listed(JsonElement page, string slug)
        => page.GetProperty("tangents").EnumerateArray().Single(tangent => tangent.GetProperty("slug").GetString() == slug);

    /// <summary>The image a listed Tangent's tile carries, or null where it carries none.</summary>
    private static string? Image(JsonElement tangent)
        => tangent.GetProperty("tile").TryGetProperty("image", out var image) ? image.GetString() : null;
}
