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
/// <c>/notifications</c> linked to the applications, on their stream, and on their door, which says how many wait; a
/// member finds none of it; a decision by one decider ends it for every other; a decider made after it arrived learns of
/// it; opening the notifications sees it, and it waits until someone decides it; admitted, the applicant's door begins
/// at their admission.</summary>
public sealed class ApplicationsReachTests
{
    private const string Leo = "did:plc:leo4reach";
    private const string Grace = "did:plc:grace4reach";
    private const string Hal = "did:plc:hal4reach";

    [Fact]
    public async Task A_moderators_count_notifications_stream_and_door_show_a_new_application_and_a_members_do_not()
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
        var unseen = door.GetProperty("who").GetProperty("notifications");
        Assert.Equal((1, false, arrival.Seq, true), (unseen.GetProperty("count").GetInt32(), unseen.GetProperty("atLeast").GetBoolean(),
            unseen.GetProperty("through").GetInt64(), unseen.GetProperty("applications").GetBoolean()));
        var waiting = door.GetProperty("applications");
        Assert.Equal((1, false, BaseUrl + ApplicationsController.Path),
            (waiting.GetProperty("count").GetInt32(), waiting.GetProperty("atLeast").GetBoolean(), waiting.GetProperty("address").GetString()));
        var doorPage = WebUtility.HtmlDecode((await Send(mo.Client, HttpMethod.Get, "/")).Body);
        Assert.Contains("<p class=\"door-offer-line\">1 application is waiting for a decision.</p>", doorPage);
        Assert.Contains($"<a class=\"tangent-new\" href=\"{BaseUrl}{ApplicationsController.Path}\">Applications</a>", doorPage);
        Assert.Contains("<span class=\"masthead-unseen-count\">1</span>", doorPage);

        var listed = (await Send(mo.Client, HttpMethod.Get, Notifications.Path, json: true)).Json;
        var entry = Assert.Single(listed.GetProperty("entries").EnumerateArray());
        Assert.Equal(("application", Grace, BaseUrl + ApplicationsController.Path, true),
            (entry.GetProperty("kind").GetString(), entry.GetProperty("who").GetProperty("id").GetString(), entry.GetProperty("address").GetString(),
                entry.GetProperty("new").GetBoolean()));
        var notifications = WebUtility.HtmlDecode((await Send(mo.Client, HttpMethod.Get, Notifications.Path)).Body);
        Assert.Contains($"<strong>grace4reach.example</strong> <a class=\"notification-link\" href=\"{BaseUrl}{ApplicationsController.Path}\">applied to join and is waiting.</a>",
            notifications);

        // The decider's poll, as their stream, carries the application with the standing the masthead's island reads.
        var polled = (await Send(mo.Client, HttpMethod.Get, $"{EventsController.Path}?{EventsController.Since}={before}", json: true)).Json;
        var row = Assert.Single(polled.GetProperty("events").EnumerateArray());
        Assert.Equal((arrival.Seq, "standing.changed", Grace, "Applied"),
            (row.GetProperty("seq").GetInt64(), row.GetProperty("kind").GetString(), row.GetProperty("actor").GetString(), row.GetProperty("standing").GetString()));

        // A member reads none of it, and neither does the applicant.
        var member = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal((0, false), (member.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32(),
            member.GetProperty("who").GetProperty("notifications").GetProperty("applications").GetBoolean()));
        Assert.False(member.TryGetProperty("applications", out _));
        Assert.Empty((await Send(ada.Client, HttpMethod.Get, Notifications.Path, json: true)).Json.GetProperty("entries").EnumerateArray());
        Assert.Empty((await Send(ada.Client, HttpMethod.Get, $"{EventsController.Path}?{EventsController.Since}={before}", json: true)).Json
            .GetProperty("events").EnumerateArray());
        var memberPages = (await Send(ada.Client, HttpMethod.Get, "/")).Body + (await Send(ada.Client, HttpMethod.Get, Notifications.Path)).Body;
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
        Assert.Equal([Hal, Grace], Applicants(await Send(leo.Client, HttpMethod.Get, Notifications.Path, json: true)));

        Assert.IsType<Outcome<Participant>.Done>(await new Admit(Leo, Grace).Send(Cancellation));

        var door = (await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal((1, 1), (door.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32(),
            door.GetProperty("applications").GetProperty("count").GetInt32()));
        Assert.Equal([Hal], Applicants(await Send(mo.Client, HttpMethod.Get, Notifications.Path, json: true)));
        Assert.Null((await Participant.Get(Grace, Cancellation))!.WaitingSeq);

        Assert.IsType<Outcome<Participant>.Done>(await new Decline(Leo, Hal).Send(Cancellation));

        door = (await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal(0, door.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32());
        Assert.False(door.TryGetProperty("applications", out _));
        Assert.Empty(Applicants(await Send(mo.Client, HttpMethod.Get, Notifications.Path, json: true)));
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
        Assert.Equal((2, 1), (door.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32(),
            door.GetProperty("applications").GetProperty("count").GetInt32()));
        var entries = (await Send(ada.Client, HttpMethod.Get, Notifications.Path, json: true)).Json.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["role", "application"], entries.Select(entry => entry.GetProperty("kind").GetString()));
        Assert.Equal(Grace, entries[1].GetProperty("who").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Opening_the_notifications_sees_an_application_which_still_waits_until_someone_decides_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Applying(tangentSpace);
        var applicant = await Applies(Grace);
        using var mo = await SignedIn(tangentSpace, Mo);
        Assert.Equal(1, Count(await Send(mo.Client, HttpMethod.Get, "/", json: true)));

        var marked = await Opened(mo, "/");
        var opened = await Send(mo.Client, HttpMethod.Get, marked.Location!);
        Assert.Equal(HttpStatusCode.OK, opened.Status);
        Assert.Contains("<button class=\"masthead-unseen\" hidden", opened.Body);
        Assert.Equal(applicant.WaitingSeq, (await Checkpoint.Get(Checkpoint.Key(Mo, Scope.Notifications), Cancellation))!.Seq);

        // Seen, it counts no more, and the door still says it waits; it is listed, no longer new.
        var door = (await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal((0, 1), (door.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32(),
            door.GetProperty("applications").GetProperty("count").GetInt32()));
        var entry = Assert.Single((await Send(mo.Client, HttpMethod.Get, Notifications.Path, json: true)).Json.GetProperty("entries").EnumerateArray());
        Assert.Equal((Grace, false), (entry.GetProperty("who").GetProperty("id").GetString(), entry.GetProperty("new").GetBoolean()));

        // A new application counts past it.
        await Applies(Hal);
        Assert.Equal(1, Count(await Send(mo.Client, HttpMethod.Get, "/", json: true)));

        // Only whoever decides it sees an application: a member's mark of it is refused.
        Assert.Equal(new Outcome<Checkpoint>.Refused(SeeNotifications.NoNotification),
            await new SeeNotifications(Ada, applicant.WaitingSeq!.Value).Send(Cancellation));
    }

    [Fact]
    public async Task An_application_older_than_a_full_read_of_rows_the_decider_may_not_view_never_skips_what_lies_between()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Applying(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Applies(Grace);
        var mine = await Started(Mo, "Lanterns", "Who lights theirs first?");
        await Replied(mine, Bea, "I do, at six.");
        // A whole read of rows that target the moderator in the studio, which he may not view, newer than both.
        var secret = await Started(Leo, "Studio notes", "Only for us.", Studio);
        Assert.IsType<Outcome<int>.Done>(await new Many(Leo, Mo, secret, Notifications.ScanLimit).Send(Cancellation));
        using var mo = await SignedIn(tangentSpace, Mo);

        var shown = new List<string>();
        var path = Notifications.Path;
        for (var pages = 0; pages < 5; pages++)
        {
            var page = (await Send(mo.Client, HttpMethod.Get, path, json: true)).Json;
            shown.AddRange(page.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("kind").GetString()!));
            if (!page.TryGetProperty("next", out var next) || next.GetString() is not { } address) break;
            path = address[BaseUrl.Length..];
        }

        Assert.Equal(["reply", "application"], shown);
        Assert.DoesNotContain("Studio notes", (await Send(mo.Client, HttpMethod.Get, Notifications.Path)).Body);
    }

    /// <summary>The writing place, whose admission says apply.</summary>
    [Fact]
    public async Task An_applicants_checkpoint_begins_at_their_admission_so_their_door_tells_it_and_nothing_posted_while_they_waited()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Applying(tangentSpace);
        await Started(Ada, "Before Grace", "Posted before she applied.");
        using var grace = await SignedIn(tangentSpace, Grace);
        Assert.Equal((await Participant.Get(Grace, Cancellation))!.WaitingSeq, (await Checkpoint.Get(Checkpoint.Key(Grace, Scope.Place), Cancellation))?.Seq);
        var waited = await Started(Ada, "While Grace waited", "Posted while she waited.");

        Assert.IsType<Outcome<Participant>.Done>(await new Admit(Mo, Grace).Send(Cancellation));
        var admission = Assert.Single(await Event.All(Cancellation), e => e is { Kind: EventKind.StandingChanged, ActorId: Mo, TargetId: Grace });
        Assert.Equal(admission.Seq - 1, (await Checkpoint.Get(Checkpoint.Key(Grace, Scope.Place), Cancellation))?.Seq);

        var door = (await Send(grace.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news");
        Assert.Equal(Mo, door.GetProperty("admitted").GetProperty("who").GetProperty("id").GetString());
        Assert.Equal(0, door.GetProperty("perTangent").GetArrayLength());
        Assert.DoesNotContain(waited.Id, door.GetRawText());

        await Replied(waited, Ada, "And one after.");
        door = (await Send(grace.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news");
        Assert.Equal(1, Assert.Single(door.GetProperty("perTangent").EnumerateArray()).GetProperty("replies").GetInt32());
    }

    private static async Task Applying(TangentSpaceFixture tangentSpace)
    {
        await Place(tangentSpace);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Admission = Admission.Apply;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));
    }

    /// <summary>A newcomer's first arrival, which applies.</summary>
    private static async Task<Participant> Applies(string id)
        => Assert.IsType<Outcome<Participant>.Done>(await new FirstArrival(id, id.Split(':')[^1] + ".example").Send(Cancellation)).Result;

    /// <summary>How many notifications the reader has not seen, as a page's JSON says.</summary>
    private static int Count(Answered page) => page.Json.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32();

    /// <summary>Who applied, for each application among a page of notifications, in its order.</summary>
    private static List<string> Applicants(Answered page) => page.Json.GetProperty("entries").EnumerateArray()
        .Where(entry => entry.GetProperty("kind").GetString() == "application")
        .Select(entry => entry.GetProperty("who").GetProperty("id").GetString()!).ToList();

    /// <summary>Appends <paramref name="Times"/> replies about <paramref name="Root"/> by <paramref name="ActorId"/> targeting
    /// <paramref name="TargetId"/>, in one transaction.</summary>
    private sealed record Many(string ActorId, string TargetId, Post Root, int Times) : ICommand<int>
    {
        public async Task<Outcome<int>> Run(CancellationToken ct)
        {
            for (var index = 0; index < Times; index++)
                await Event.Append(EventKind.PostCreated, DateTimeOffset.UtcNow, ct, tangentId: Root.TangentId, topicId: Root.Id, postId: Root.Id,
                    actorId: ActorId, targetId: TargetId);
            return new Outcome<int>.Done(Times);
        }
    }
}
