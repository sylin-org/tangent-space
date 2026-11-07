using System.Net;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Participants;

/// <summary>An application reaches whoever decides it: on a place that says apply, a newcomer's arrival writes one event
/// and their row, whoever holds ManageStanding; whoever holds it when they read finds the application in their count, on
/// their stream, and on their door, which says how many wait, linked to the applications; a member finds none of it; a
/// decision by one decider ends it for every other; a decider made after it arrived learns of it; saying seen takes it
/// out of the count, and it waits until someone decides it; admitted, the applicant's door tells it, and what was
/// written since they arrived.</summary>
public sealed class ApplicationsReachTests
{
    private static readonly string Leo = Persona.Id("leo4reach");
    private static readonly string Grace = Persona.Id("grace4reach");
    private static readonly string Hal = Persona.Id("hal4reach");

    [Fact]
    public async Task A_moderators_count_stream_and_door_show_a_new_application_and_a_members_do_not()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Applying(tangentSpace);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var ada = await SignedIn(tangentSpace, Ada);
        var before = (await Event.All(Cancellation)).Select(e => e.Seq).DefaultIfEmpty(0).Max();

        var applicant = await Applies(Grace);

        // One row for the applicant and one event, however many may decide it.
        var arrival = Assert.Single(await Event.All(Cancellation), e => e.Seq > before);
        Assert.Equal((EventKind.StandingChanged, Grace, Grace, Standing.Applied), (arrival.Kind, arrival.ActorId, arrival.TargetId, arrival.Standing));
        Assert.Equal(arrival.Seq, applicant.WaitingSeq);

        var door = (await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json;
        var unseen = door.GetProperty("who").GetProperty("forYou");
        Assert.Equal((1, false, arrival.Seq, true), (unseen.GetProperty("count").GetInt32(), unseen.GetProperty("atLeast").GetBoolean(),
            unseen.GetProperty("through").GetInt64(), unseen.GetProperty("applications").GetBoolean()));
        var waiting = door.GetProperty("applications");
        Assert.Equal((1, false, ApplicationsController.WaitingAddress(BaseUrl)),
            (waiting.GetProperty("count").GetInt32(), waiting.GetProperty("atLeast").GetBoolean(), waiting.GetProperty("address").GetString()));
        var doorPage = WebUtility.HtmlDecode((await Send(mo.Client, HttpMethod.Get, "/")).Body);
        Assert.Contains("<p class=\"door-offer-line\">1 application is waiting for a decision.</p>", doorPage);
        Assert.Contains($"<a class=\"tangent-new\" href=\"{ApplicationsController.WaitingAddress(BaseUrl)}\">Applications</a>", doorPage);
        Assert.Contains("<span class=\"masthead-unseen-count\">1</span>", doorPage);

        // The decider's poll, as their stream, carries the application with the standing the masthead's island reads.
        var polled = (await Send(mo.Client, HttpMethod.Get, $"{EventsController.Path}?{EventsController.Since}={before}", json: true)).Json;
        var row = Assert.Single(polled.GetProperty("events").EnumerateArray());
        Assert.Equal((arrival.Seq, "standing.changed", Grace, "Applied"),
            (row.GetProperty("seq").GetInt64(), row.GetProperty("kind").GetString(), row.GetProperty("actor").GetString(), row.GetProperty("standing").GetString()));

        // A member reads none of it, and neither does the applicant.
        var member = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal((0, false), (member.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32(),
            member.GetProperty("who").GetProperty("forYou").GetProperty("applications").GetBoolean()));
        Assert.False(member.TryGetProperty("applications", out _));
        Assert.Empty((await Send(ada.Client, HttpMethod.Get, $"{EventsController.Path}?{EventsController.Since}={before}", json: true)).Json
            .GetProperty("events").EnumerateArray());
        var memberPages = (await Send(ada.Client, HttpMethod.Get, "/")).Body;
        Assert.DoesNotContain("grace4reach", memberPages);
        Assert.DoesNotContain("waiting for a decision", memberPages);
        using var grace = await SignedIn(tangentSpace, Grace);
        Assert.Equal(0, Count(await Send(grace.Client, HttpMethod.Get, "/", json: true)));
    }

    [Fact]
    public async Task A_decision_by_one_decider_ends_the_application_for_every_other()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Applying(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Applies(Grace);
        await Applies(Hal);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var leo = await SignedIn(tangentSpace, Leo);
        Assert.Equal((2, 2), (Count(await Send(mo.Client, HttpMethod.Get, "/", json: true)), Count(await Send(leo.Client, HttpMethod.Get, "/", json: true))));

        Assert.IsType<Outcome<Participant>.Done>(await new Admit(Leo, Grace).Send(Cancellation));

        var door = (await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal((1, 1), (door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32(),
            door.GetProperty("applications").GetProperty("count").GetInt32()));
        Assert.Null((await Participant.Get(Grace, Cancellation))!.WaitingSeq);

        Assert.IsType<Outcome<Participant>.Done>(await new Decline(Leo, Hal).Send(Cancellation));

        door = (await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal(0, door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32());
        Assert.False(door.TryGetProperty("applications", out _));
        Assert.DoesNotContain("waiting for a decision", (await Send(mo.Client, HttpMethod.Get, "/")).Body);
        // The declined application stays among the applications, where it can still be admitted.
        Assert.Equal(Standing.Declined, (await Participant.Get(Hal, Cancellation))!.Standing);
        Assert.Single((await Send(mo.Client, HttpMethod.Get, ApplicationsController.Path, json: true)).Json.GetProperty("applications").EnumerateArray());
    }

    [Fact]
    public async Task A_decider_made_a_moderator_after_the_application_arrived_still_learns_of_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Applying(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var ada = await SignedIn(tangentSpace, Ada);
        await Applies(Grace);
        Assert.Equal(0, Count(await Send(ada.Client, HttpMethod.Get, "/", json: true)));

        Assert.IsType<Outcome<Participant>.Done>(await new ChangeRole(Leo, Ada, Roles.Moderator, Give: true).Send(Cancellation));

        var door = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal((2, 1), (door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32(),
            door.GetProperty("applications").GetProperty("count").GetInt32()));
        Assert.Equal("moderator", Assert.Single(door.GetProperty("news").GetProperty("roles").EnumerateArray()).GetProperty("role").GetString());
    }

    [Fact]
    public async Task Seen_takes_an_application_out_of_the_count_and_it_still_waits_until_someone_decides_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Applying(tangentSpace);
        var applicant = await Applies(Grace);
        using var mo = await SignedIn(tangentSpace, Mo);
        var door = (await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal(1, door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32());
        var through = Valued(door.GetProperty("news").GetProperty("seen"), SeenController.ThroughField)!;
        Assert.Equal(applicant.WaitingSeq!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), through);

        var seen = await Form(mo, "/", SeenController.Path, new Dictionary<string, string> { [SeenController.ThroughField] = through });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/"), (seen.Status, seen.Location));
        Assert.Contains("<a class=\"masthead-unseen\" href=\"" + BaseUrl + "/\" hidden", (await Send(mo.Client, HttpMethod.Get, "/")).Body);

        // Seen, it counts no more, and the door still says it waits.
        door = (await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal((0, 1), (door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32(),
            door.GetProperty("applications").GetProperty("count").GetInt32()));

        // A new application counts past it.
        await Applies(Hal);
        Assert.Equal(1, Count(await Send(mo.Client, HttpMethod.Get, "/", json: true)));
    }

    [Fact]
    public async Task An_admitted_applicants_door_tells_their_admission_and_what_was_written_since_they_arrived()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Applying(tangentSpace);
        var before = await Started(Ada, "Before Grace", "Posted before she applied.");
        using var grace = await SignedIn(tangentSpace, Grace);
        var arrived = (await Participant.Get(Grace, Cancellation))!;
        Assert.Equal(arrived.WaitingSeq, arrived.ArrivedSeq);
        var waited = await Started(Ada, "While Grace waited", "Posted while she waited.");

        Assert.IsType<Outcome<Participant>.Done>(await new Admit(Mo, Grace).Send(Cancellation));
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(Grace, Scope.Place), Cancellation));

        var door = (await Send(grace.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news");
        Assert.Equal(Mo, door.GetProperty("admitted").GetProperty("who").GetProperty("id").GetString());
        Assert.Equal(1, Assert.Single(door.GetProperty("perTangent").EnumerateArray()).GetProperty("topics").GetInt32());
        Assert.DoesNotContain(before.Id, door.GetRawText());
        var alive = (await Send(grace.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("alive");
        Assert.True(alive.EnumerateArray().Single(shown => shown.GetProperty("topic").GetProperty("id").GetString() == waited.Id).GetProperty("topic").GetProperty("new").GetBoolean());
        Assert.False(alive.EnumerateArray().Single(shown => shown.GetProperty("topic").GetProperty("id").GetString() == before.Id).GetProperty("topic").GetProperty("new").GetBoolean());
    }

    /// <summary>The writing place, whose admission says apply.</summary>
    private static async Task Applying(TangentSpaceFixture tangentSpace)
    {
        await Place(tangentSpace);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Admission = Admission.Apply;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));
    }

    /// <summary>A newcomer's first arrival, which applies.</summary>
    private static async Task<Participant> Applies(string id)
        => Assert.IsType<Outcome<Participant>.Done>(await new FirstArrival(id, Persona.Did(id), Persona.Handle(id)).Send(Cancellation)).Result;

    /// <summary>How many things are for the reader and new, as a page's JSON says.</summary>
    private static int Count(Answered page) => page.Json.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32();
}
