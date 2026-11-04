using System.Net;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>A report reaches whoever may act on it: a report writes one event, however many may view the moderators'
/// Tangent; whoever may view it when they read finds the report in their count, on their door linked to the report's
/// Topic, and on their stream; a member, the reporter and the reported author find none of it, not even a position that
/// moved; someone made a moderator after it arrived learns of it; and reading the report's Topic, or saying seen, takes
/// it out of the count, which a new report raises again.</summary>
public sealed class ReportsReachTests
{
    private const string Leo = "did:plc:leo4reportsreach";

    [Fact]
    public async Task A_moderators_count_and_door_show_a_new_report_while_a_members_the_reporters_and_the_authors_do_not()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Reporting(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var topic = await Started(Bea, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(topic, Bea, "Whoever is slowest, obviously.");
        using var mo = await SignedIn(tangentSpace, Mo);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        var before = (await Event.All(Cancellation)).Max(e => e.Seq);

        var report = await Reported(Ada, reply, "This mocks someone in the workshop.");

        // One event, however many may view the moderators' Tangent, and it targets no one.
        var created = Assert.Single(await Event.All(Cancellation), e => e.Seq > before);
        Assert.Equal((EventKind.PostCreated, Ada, (string?)null, (bool?)true), (created.Kind, created.ActorId, created.TargetId, created.Report));

        foreach (var reader in new[] { mo, leo })
        {
            var unseen = (await Send(reader.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("who").GetProperty("forYou");
            Assert.Equal((1, false, created.Seq), (unseen.GetProperty("count").GetInt32(), unseen.GetProperty("atLeast").GetBoolean(),
                unseen.GetProperty("through").GetInt64()));
            Assert.Contains("<span class=\"masthead-unseen-count\">1</span>", (await Send(reader.Client, HttpMethod.Get, "/")).Body);
        }

        var entry = Assert.Single((await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("reports").GetProperty("posts").EnumerateArray());
        Assert.Equal((Ada, BaseUrl + TopicPath(report)), (entry.GetProperty("who").GetProperty("id").GetString(), entry.GetProperty("address").GetString()));
        Assert.Equal((ModeratorsTangent.Name, "Report: Lanterns at dusk", "This mocks someone in the workshop."),
            (entry.GetProperty("tangent").GetString(), entry.GetProperty("topic").GetString(), entry.GetProperty("line").GetString()));
        var page = WebUtility.HtmlDecode((await Send(mo.Client, HttpMethod.Get, "/")).Body);
        Assert.Contains("Reports · 1", page);
        Assert.Contains($"href=\"{BaseUrl}{TopicPath(report)}\"", page);

        // The moderator's poll, as their stream, carries the report as one, which is what the masthead's island counts.
        var row = Assert.Single((await Send(mo.Client, HttpMethod.Get, $"{EventsController.Path}?{EventsController.Since}={before}", json: true)).Json
            .GetProperty("events").EnumerateArray());
        Assert.Equal((created.Seq, EventKind.PostCreated, Ada, true),
            (row.GetProperty("seq").GetInt64(), row.GetProperty("kind").GetString(), row.GetProperty("actor").GetString(), row.GetProperty("report").GetBoolean()));

        // The reporter, a member, and the reported author read none of it.
        foreach (var reader in new[] { ada, bea })
        {
            var door = await Send(reader.Client, HttpMethod.Get, "/", json: true);
            Assert.Equal(0, Count(door));
            Assert.Equal(0, door.Json.GetProperty("news").GetProperty("reports").GetProperty("count").GetInt32());
            Assert.DoesNotContain("Reports ·", (await Send(reader.Client, HttpMethod.Get, "/")).Body);
        }
    }

    [Fact]
    public async Task Someone_made_a_moderator_after_the_report_arrived_learns_of_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Reporting(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var topic = await Started(Leo, "Kiln schedule", "Firing on Thursday.");
        using var ada = await SignedIn(tangentSpace, Ada);
        await Reported(Bea, topic, "Off topic for the workshop.");
        Assert.Equal(0, Count(await Send(ada.Client, HttpMethod.Get, "/", json: true)));

        Assert.IsType<Outcome<Participant>.Done>(await new ChangeRole(Leo, Ada, Roles.Moderator, Give: true).Send(Cancellation));

        var door = await Send(ada.Client, HttpMethod.Get, "/", json: true);
        Assert.Equal(2, Count(door));
        var news = door.Json.GetProperty("news");
        Assert.Equal("moderator", Assert.Single(news.GetProperty("roles").EnumerateArray()).GetProperty("role").GetString());
        Assert.Equal(Bea, Assert.Single(news.GetProperty("reports").GetProperty("posts").EnumerateArray()).GetProperty("who").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Reading_the_reports_topic_or_saying_seen_takes_it_out_of_the_count_and_a_new_one_counts_past_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Reporting(tangentSpace);
        var topic = await Started(Bea, "Lanterns at dusk", "Who lights theirs first?");
        var report = await Reported(Ada, topic, "Unkind.");
        using var mo = await SignedIn(tangentSpace, Mo);
        Assert.Equal(1, Count(await Send(mo.Client, HttpMethod.Get, "/", json: true)));

        // Read, the report counts no more.
        var read = (await Send(mo.Client, HttpMethod.Get, TopicPath(report), json: true)).Json.GetProperty("seen");
        var marked = await Form(mo, TopicPath(report), SeenController.Path, new Dictionary<string, string>
        {
            [SeenController.TopicField] = report.Id,
            [SeenController.ThroughField] = Valued(read, SeenController.ThroughField)!
        });
        Assert.Equal(HttpStatusCode.SeeOther, marked.Status);
        Assert.Contains("<a class=\"masthead-unseen\" href=\"" + BaseUrl + "/\" hidden", (await Send(mo.Client, HttpMethod.Get, "/")).Body);
        Assert.Equal(0, Count(await Send(mo.Client, HttpMethod.Get, "/", json: true)));

        // A new report counts past it, and seen from the door takes it out too.
        var reply = await Replied(topic, Bea, "Whoever is slowest, obviously.");
        await Reported(Ada, reply, "Unkind again.");
        var door = await Send(mo.Client, HttpMethod.Get, "/", json: true);
        Assert.Equal(1, Count(door));
        await Form(mo, "/", SeenController.Path, new Dictionary<string, string>
        {
            [SeenController.ThroughField] = Valued(door.Json.GetProperty("news").GetProperty("seen"), SeenController.ThroughField)!
        });
        Assert.Equal(0, Count(await Send(mo.Client, HttpMethod.Get, "/", json: true)));
    }

    [Fact]
    public async Task Nothing_of_a_report_reaches_a_non_moderator_through_the_count_its_through_the_poll_or_the_stream()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Reporting(tangentSpace);
        var topic = await Started(Ada, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(topic, Bea, "Whoever is slowest, obviously.");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        var before = (await Event.All(Cancellation)).Max(e => e.Seq);
        var positions = new Dictionary<string, (long Through, long Poll)>();
        foreach (var (id, reader) in new[] { (Ada, ada), (Bea, bea) }) positions[id] = Positions(await Send(reader.Client, HttpMethod.Get, "/", json: true));

        await Reported(Ada, reply, "This mocks someone in the workshop.");

        // The reporter and the reported author: the same count, the count's through and the door's poll as before it.
        foreach (var (id, reader) in new[] { (Ada, ada), (Bea, bea) })
        {
            var door = await Send(reader.Client, HttpMethod.Get, "/", json: true);
            Assert.Equal(id == Ada ? 1 : 0, Count(door));
            Assert.Equal(positions[id], Positions(door));
            var polled = (await Send(reader.Client, HttpMethod.Get, $"{EventsController.Path}?{EventsController.Since}={before}", json: true)).Json;
            Assert.Empty(polled.GetProperty("events").EnumerateArray());
            Assert.Equal(before, polled.GetProperty("through").GetInt64());
        }

        // Ada's stream from before the report sends the next row that reaches her, and nothing of the report.
        await using var stream = await Streamed.Open(ada.Client, $"{EventsController.Path}?{EventsController.Since}={before}");
        Assert.Equal("open", (await stream.Any()).Comment);
        var next = await Replied(topic, Bea, "And last, the lighthouse.");
        var frame = await stream.Next();
        Assert.Equal(next.Id, frame.Json.GetProperty("post").GetString());
        Assert.False(frame.Json.TryGetProperty("report", out _));
    }

    /// <summary>The place every proof of the write verbs starts from, with the moderators' Tangent a start creates for it.</summary>
    private static async Task Reporting(TangentSpaceFixture tangentSpace)
    {
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new CreateModeratorsTangent().Send(Cancellation));
    }

    private static async Task<Post> Reported(string reporter, Post post, string reason)
        => Assert.IsType<Outcome<Post>.Done>(await new ReportPost(Tids.Mint(), post.Id, PostBody.Of(reason), reporter).Send(Cancellation)).Result;

    /// <summary>How many things are for the reader and new, as a page's JSON says.</summary>
    private static int Count(Answered page) => page.Json.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32();

    /// <summary>The positions a door gives its reader: where their count reads through, and where its poll begins.</summary>
    private static (long Through, long Poll) Positions(Answered door)
        => (door.Json.GetProperty("who").GetProperty("forYou").GetProperty("through").GetInt64(),
            door.Json.GetProperty("poll").GetProperty("through").GetInt64());
}
