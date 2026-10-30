using System.Net;
using Koan.Data.Core;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Space;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>A report reaches whoever may act on it: a report writes one event, however many may view the moderators'
/// Tangent; whoever may view it when they read finds the report in their count, on <c>/notifications</c> linked to the
/// report's Topic, and on their stream; a member, the reporter and the reported author find none of it, not even a
/// position that moved; someone made a moderator after it arrived learns of it; opening the notifications sees it; and a
/// report older than a full read of rows the moderator may not view is never skipped.</summary>
public sealed class ReportsReachTests
{
    private const string Leo = "did:plc:leo4reportsreach";

    [Fact]
    public async Task A_moderators_bell_and_notifications_show_a_new_report_while_a_members_the_reporters_and_the_authors_do_not()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Reporting(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var topic = await Started(writer, Bea, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(writer, topic, Bea, "Whoever is slowest, obviously.");
        using var mo = await SignedIn(tangentSpace, Mo);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        var before = (await Event.All(Cancellation)).Max(e => e.Seq);

        var report = await Reported(writer, Ada, reply, "This mocks someone in the workshop.");

        // One event, however many may view the moderators' Tangent, and it targets no one.
        var created = Assert.Single(await Event.All(Cancellation), e => e.Seq > before);
        Assert.Equal((EventKind.PostCreated, Ada, (string?)null, (bool?)true), (created.Kind, created.ActorId, created.TargetId, created.Report));

        foreach (var reader in new[] { mo, leo })
        {
            var unseen = (await Send(reader.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("who").GetProperty("notifications");
            Assert.Equal((1, false, created.Seq), (unseen.GetProperty("count").GetInt32(), unseen.GetProperty("atLeast").GetBoolean(),
                unseen.GetProperty("through").GetInt64()));
            Assert.Contains("<span class=\"masthead-unseen-count\">1</span>", (await Send(reader.Client, HttpMethod.Get, "/")).Body);
        }

        var entry = Assert.Single((await Send(mo.Client, HttpMethod.Get, Notifications.Path, json: true)).Json.GetProperty("entries").EnumerateArray());
        Assert.Equal(("report", Ada, BaseUrl + TopicPath(report), true, Bea),
            (entry.GetProperty("kind").GetString(), entry.GetProperty("who").GetProperty("id").GetString(), entry.GetProperty("address").GetString(),
                entry.GetProperty("new").GetBoolean(), entry.GetProperty("reported").GetProperty("id").GetString()));
        Assert.Equal((ModeratorsTangent.Name, "Report: Lanterns at dusk", "This mocks someone in the workshop."),
            (entry.GetProperty("tangent").GetString(), entry.GetProperty("topic").GetString(), entry.GetProperty("line").GetString()));
        var page = WebUtility.HtmlDecode((await Send(mo.Client, HttpMethod.Get, Notifications.Path)).Body);
        Assert.Contains($"<strong>{entry.GetProperty("who").GetProperty("name").GetString()}</strong> <a class=\"notification-link\" " +
                        $"href=\"{BaseUrl}{TopicPath(report)}\">reported a post by {entry.GetProperty("reported").GetProperty("name").GetString()}</a>", page);

        // The moderator's poll, as their stream, carries the report as one, which is what the masthead's island counts.
        var row = Assert.Single((await Send(mo.Client, HttpMethod.Get, $"{EventsController.Path}?{EventsController.Since}={before}", json: true)).Json
            .GetProperty("events").EnumerateArray());
        Assert.Equal((created.Seq, EventKind.PostCreated, Ada, true),
            (row.GetProperty("seq").GetInt64(), row.GetProperty("kind").GetString(), row.GetProperty("actor").GetString(), row.GetProperty("report").GetBoolean()));

        // The reporter, a member, and the reported author read none of it.
        foreach (var reader in new[] { ada, bea })
        {
            Assert.Equal(0, Count(await Send(reader.Client, HttpMethod.Get, "/", json: true)));
            Assert.Empty((await Send(reader.Client, HttpMethod.Get, Notifications.Path, json: true)).Json.GetProperty("entries").EnumerateArray());
            Assert.DoesNotContain("reported a post", (await Send(reader.Client, HttpMethod.Get, Notifications.Path)).Body);
        }
    }

    [Fact]
    public async Task Someone_made_a_moderator_after_the_report_arrived_learns_of_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Reporting(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var topic = await Started(writer, Leo, "Kiln schedule", "Firing on Thursday.");
        using var ada = await SignedIn(tangentSpace, Ada);
        await Reported(writer, Bea, topic, "Off topic for the workshop.");
        Assert.Equal(0, Count(await Send(ada.Client, HttpMethod.Get, "/", json: true)));

        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new ChangeRole(Leo, Ada, Roles.Moderator, Give: true), Cancellation));

        Assert.Equal(2, Count(await Send(ada.Client, HttpMethod.Get, "/", json: true)));
        var entries = (await Send(ada.Client, HttpMethod.Get, Notifications.Path, json: true)).Json.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(["role", "report"], entries.Select(entry => entry.GetProperty("kind").GetString()));
        Assert.Equal((Bea, Leo), (entries[1].GetProperty("who").GetProperty("id").GetString(), entries[1].GetProperty("reported").GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Opening_the_notifications_sees_a_report_and_a_new_one_counts_past_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Reporting(tangentSpace);
        var topic = await Started(writer, Bea, "Lanterns at dusk", "Who lights theirs first?");
        var report = await Reported(writer, Ada, topic, "Unkind.");
        var created = Assert.Single(await Event.Query(e => e.PostId == report.Id, Cancellation));
        using var mo = await SignedIn(tangentSpace, Mo);
        Assert.Equal(1, Count(await Send(mo.Client, HttpMethod.Get, "/", json: true)));

        var opened = await Send(mo.Client, HttpMethod.Get, Notifications.Path);
        Assert.Equal(HttpStatusCode.OK, opened.Status);
        Assert.Contains($"<a class=\"masthead-unseen\" href=\"{BaseUrl}/notifications\" hidden", opened.Body);
        await Reached.Settled(tangentSpace, Cancellation);
        Assert.Equal(created.Seq, (await Checkpoint.Get(Checkpoint.Key(Mo, Scope.Notifications), Cancellation))!.Seq);

        // Seen, it counts no more and is listed, no longer new; a new report counts past it.
        Assert.Equal(0, Count(await Send(mo.Client, HttpMethod.Get, "/", json: true)));
        Assert.False(Assert.Single((await Send(mo.Client, HttpMethod.Get, Notifications.Path, json: true)).Json.GetProperty("entries").EnumerateArray())
            .GetProperty("new").GetBoolean());
        var reply = await Replied(writer, topic, Bea, "Whoever is slowest, obviously.");
        await Reported(writer, Ada, reply, "Unkind again.");
        Assert.Equal(1, Count(await Send(mo.Client, HttpMethod.Get, "/", json: true)));

        // Only whoever may view the moderators' Tangent sees a report: a member's mark of it is refused.
        Assert.Equal(new Outcome<Checkpoint>.Refused(SeeNotifications.NoNotification),
            await writer.Send(new SeeNotifications(Bea, created.Seq), Cancellation));
    }

    [Fact]
    public async Task Nothing_of_a_report_reaches_a_non_moderator_through_the_count_its_through_the_poll_or_the_stream()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Reporting(tangentSpace);
        var topic = await Started(writer, Ada, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(writer, topic, Bea, "Whoever is slowest, obviously.");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        var before = (await Event.All(Cancellation)).Max(e => e.Seq);
        var positions = new Dictionary<string, (long Through, long Poll)>();
        foreach (var (id, reader) in new[] { (Ada, ada), (Bea, bea) }) positions[id] = Positions(await Send(reader.Client, HttpMethod.Get, "/", json: true));

        await Reported(writer, Ada, reply, "This mocks someone in the workshop.");

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
        var next = await Replied(writer, topic, Bea, "And last, the lighthouse.");
        var frame = await stream.Next();
        Assert.Equal(next.Id, frame.Json.GetProperty("post").GetString());
        Assert.False(frame.Json.TryGetProperty("report", out _));
    }

    [Fact]
    public async Task A_report_older_than_a_full_read_of_rows_the_moderator_may_not_view_is_never_skipped()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Reporting(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var lanterns = await Started(writer, Bea, "Lanterns at dusk", "Who lights theirs first?");
        await Reported(writer, Ada, lanterns, "Unkind.");
        var mine = await Started(writer, Mo, "Kiln schedule", "Firing on Thursday.");
        await Replied(writer, mine, Bea, "I bring the glazes.");
        // A whole read of rows that target the moderator in the studio, which he may not view, newer than both.
        var secret = await Started(writer, Leo, "Studio notes", "Only for us.", Studio);
        Assert.IsType<Outcome<int>.Done>(await writer.Send(new Many(Leo, Mo, secret, Notifications.ScanLimit), Cancellation));
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

        Assert.Equal(["reply", "report"], shown);
        Assert.DoesNotContain("Studio notes", (await Send(mo.Client, HttpMethod.Get, Notifications.Path)).Body);
    }

    /// <summary>The place every proof of the write verbs starts from, with the moderators' Tangent a start creates for it.</summary>
    private static async Task<WriteLoop> Reporting(TangentSpaceFixture tangentSpace)
    {
        var writer = await Place(tangentSpace);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(new CreateModeratorsTangent(), Cancellation));
        return writer;
    }

    private static async Task<Post> Reported(WriteLoop writer, string reporter, Post post, string reason)
        => Assert.IsType<Outcome<Post>.Done>(await writer.Send(new ReportPost(Tids.Mint(), post.Id, PostBody.Of(reason), reporter), Cancellation)).Result;

    /// <summary>How many notifications the reader has not seen, as a page's JSON says.</summary>
    private static int Count(Answered page) => page.Json.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32();

    /// <summary>The positions a door gives its reader: where their count reads through, and where its poll begins.</summary>
    private static (long Through, long Poll) Positions(Answered door)
        => (door.Json.GetProperty("who").GetProperty("notifications").GetProperty("through").GetInt64(),
            door.Json.GetProperty("poll").GetProperty("through").GetInt64());

    /// <summary>Appends <paramref name="Times"/> replies about <paramref name="Root"/> by <paramref name="ActorId"/> targeting
    /// <paramref name="TargetId"/>, in one transaction.</summary>
    private sealed record Many(string ActorId, string TargetId, Post Root, int Times) : ICommand<int>
    {
        public async Task<Outcome<int>> Run(Sequences sequences, CancellationToken ct)
        {
            for (var index = 0; index < Times; index++)
                await Event.Next(sequences, EventKind.PostCreated, DateTimeOffset.UtcNow, tangentId: Root.TangentId, topicId: Root.Id, postId: Root.Id,
                    actorId: ActorId, targetId: TargetId).Save(ct);
            return new Outcome<int>.Done(Times);
        }
    }
}
