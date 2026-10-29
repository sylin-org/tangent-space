using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Events;

/// <summary>The log for a reader, live and polled: a reply reaches an open stream with its sequence as the frame's id,
/// and a reconnect names the last one and loses nothing; what a reader may not view never reaches them, and stops
/// reaching them once they lose it; the poll answers the same rows a page at a time and says where to ask again, as the
/// door does; a slow stream is closed at its queue's bound and a reader's streams are capped.</summary>
public sealed class StreamTests
{
    private const string Leo = "did:plc:leo4live";

    [Fact]
    public async Task A_reply_committed_after_a_stream_opened_reaches_it_with_its_sequence_as_id_and_a_reconnect_with_last_event_id_resumes_without_a_gap_or_a_repeat()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var root = await Started(writer, Bea, "Lanterns at dusk", "Who lights theirs first?");

        Streamed.Frame first;
        await using (var stream = await Streamed.Open(ada.Client, EventsController.Path))
        {
            Assert.Equal(HttpStatusCode.OK, stream.Response.StatusCode);
            Assert.Equal(EventsController.StreamType, stream.Response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("open", (await stream.Any()).Comment);

            var reply = await Replied(writer, root, Bea, "I do, at six.");
            first = await stream.Next();

            var created = await Created(reply);
            Assert.Equal((EventKind.PostCreated, created.Seq, created.Seq),
                (first.Event, first.Seq, first.Json.GetProperty("seq").GetInt64()));
            Assert.Equal((reply.Id, root.Id, Workshop, Bea),
                (first.Json.GetProperty("post").GetString(), first.Json.GetProperty("topic").GetString(),
                    first.Json.GetProperty("tangent").GetString(), first.Json.GetProperty("actor").GetString()));
        }

        var second = await Replied(writer, root, Bea, "Then the harbour.");
        var third = await Replied(writer, root, Bea, "Then the hill.");

        // The header wins over the query, as a browser's reconnect sends both.
        await using var resumed = await Streamed.Open(ada.Client, EventsController.Path + "?since=0", lastEventId: first.Id);
        Assert.Equal("open", (await resumed.Any()).Comment);
        var frames = new List<Streamed.Frame> { await resumed.Next(), await resumed.Next() };
        var fourth = await Replied(writer, root, Bea, "And last, the lighthouse.");
        frames.Add(await resumed.Next());

        Assert.Equal(new string?[] { second.Id, third.Id, fourth.Id }, frames.Select(frame => frame.Json.GetProperty("post").GetString()));
        var logged = (await Event.All(Cancellation)).Where(e => e.Seq > first.Seq && e.Seq <= frames[^1].Seq && e.TangentId is not null)
            .OrderBy(e => e.Seq).Select(e => e.Seq);
        Assert.Equal(logged, frames.Select(frame => frame.Seq));
    }

    [Fact]
    public async Task An_event_in_a_tangent_the_reader_may_not_view_never_reaches_their_stream_or_poll_and_one_they_lose_view_of_stops_reaching_them_within_the_refresh()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var ada = await SignedIn(tangentSpace, Ada);
        await using var stream = await Streamed.Open(ada.Client, EventsController.Path);
        Assert.Equal("open", (await stream.Any()).Comment);

        var hidden = await Started(writer, Leo, "Studio notes", "Only for us.", Studio);
        var open = await Started(writer, Bea, "An open question", "Anyone?");

        Assert.Equal(open.Id, (await stream.Next()).Json.GetProperty("post").GetString());
        var before = (await Created(hidden)).Seq - 1;
        foreach (var client in new[] { ada.Client, Agent(tangentSpace) })
        {
            var poll = (await Send(client, HttpMethod.Get, EventsController.Path + "?since=" + before, json: true)).Json;
            Assert.Equal(new string?[] { open.Id }, poll.GetProperty("events").EnumerateArray().Select(row => row.GetProperty("post").GetString()));
            Assert.Equal((await Created(open)).Seq, poll.GetProperty("through").GetInt64());
        }

        // The workshop closes to everyone but administrators; no row of the log says so, so the stream learns it when
        // it next reads what Ada holds, at most a refresh later.
        await Overwrites.SetAudience(tangentSpace.Services.GetRequiredService<RoleCollection>(), Workshop, Audience.Private, Cancellation);
        await Task.Delay(Readable.Refresh + TimeSpan.FromMilliseconds(500), Cancellation);
        var closed = await Started(writer, Leo, "Behind the door", "Administrators only now.");
        var article = await Started(writer, Leo, "A public piece", "Still for everyone.", Articles);

        Assert.Equal(article.Id, (await stream.Next()).Json.GetProperty("post").GetString());
        var after = (await Send(ada.Client, HttpMethod.Get, EventsController.Path + "?since=" + ((await Created(closed)).Seq - 1), json: true)).Json;
        Assert.Equal(new string?[] { article.Id }, after.GetProperty("events").EnumerateArray().Select(row => row.GetProperty("post").GetString()));
    }

    [Fact]
    public async Task The_poll_answers_the_streams_rows_a_bounded_page_at_a_time_with_where_to_ask_next_and_the_door_says_where_to_ask_again()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var head = (await Send(ada.Client, HttpMethod.Get, EventsController.Path, json: true)).Json;
        Assert.Empty(head.GetProperty("events").EnumerateArray());
        var since = head.GetProperty("through").GetInt64();
        Assert.Equal(BaseUrl + "/events?since=" + since, head.GetProperty("next").GetString());

        var root = await Started(writer, Bea, "Counting lanterns", "One.");
        for (var count = 2; count <= EventsController.PageSize + 6; count++) await Replied(writer, root, Bea, count + ".");

        var first = (await Send(ada.Client, HttpMethod.Get, EventsController.Path + "?since=" + since, json: true)).Json;
        var rows = first.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal((since, EventsController.PageSize, true), (first.GetProperty("since").GetInt64(), rows.Count, first.GetProperty("more").GetBoolean()));
        var through = rows[^1].GetProperty("seq").GetInt64();
        Assert.Equal((through, BaseUrl + "/events?since=" + through), (first.GetProperty("through").GetInt64(), first.GetProperty("next").GetString()));
        var second = (await Send(ada.Client, HttpMethod.Get, EventsController.Path + "?since=" + through, json: true)).Json;
        rows.AddRange(second.GetProperty("events").EnumerateArray());
        Assert.Equal((6, false), (second.GetProperty("events").GetArrayLength(), second.GetProperty("more").GetBoolean()));

        await using (var stream = await Streamed.Open(ada.Client, EventsController.Path + "?since=" + since))
        {
            foreach (var row in rows)
            {
                var frame = await stream.Next();
                Assert.Equal(row.GetProperty("seq").GetInt64(), frame.Seq);
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(row.GetRawText()), JsonNode.Parse(frame.Data)), frame.Data + " / " + row.GetRawText());
            }
        }

        var last = rows[^1].GetProperty("seq").GetInt64();
        foreach (var client in new[] { ada.Client, Agent(tangentSpace) })
        {
            var poll = (await Send(client, HttpMethod.Get, "/", json: true)).Json.GetProperty("poll");
            Assert.Equal((last, BaseUrl + "/events?since=" + last), (poll.GetProperty("through").GetInt64(), poll.GetProperty("next").GetString()));
        }
    }

    [Fact]
    public async Task A_slow_stream_is_closed_at_its_queue_bound_a_reader_resumes_by_since_and_a_readers_streams_are_capped()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var streams = tangentSpace.Services.GetRequiredService<EventStream>();

        var aborted = false;
        var slow = streams.Open("a slow reader", () => aborted = true)!;
        var row = new Event { Id = Event.Key(1), Seq = 1, Kind = EventKind.PostCreated, TangentId = Workshop, At = DateTimeOffset.UtcNow };
        for (var count = 0; count < EventStream.QueueBound; count++) streams.Publish(row);
        Assert.Equal((false, false, 1), (slow.Slow, aborted, streams.Count("a slow reader").Reader));
        streams.Publish(row);
        Assert.Equal((true, true, 0), (slow.Slow, aborted, streams.Count("a slow reader").Reader));
        var queued = 0;
        while (slow.Queue.TryRead(out _)) queued++;
        Assert.Equal(EventStream.QueueBound, queued);
        Assert.True(slow.Queue.Completion.IsCompleted);

        var root = await Started(writer, Bea, "Where were we", "Here.");
        var reply = await Replied(writer, root, Bea, "And here.");
        await using (var resumed = await Streamed.Open(ada.Client, EventsController.Path + "?since=" + (await Created(root)).Seq))
            Assert.Equal(reply.Id, (await resumed.Next()).Json.GetProperty("post").GetString());

        // The server gives a stream's place back once it sees the stream's request end.
        await Released(streams);
        var held = new List<Streamed>();
        try
        {
            for (var count = 0; count < EventStream.PerReader; count++)
            {
                held.Add(await Streamed.Open(ada.Client, EventsController.Path));
                Assert.Equal(HttpStatusCode.OK, held[^1].Response.StatusCode);
            }

            await using (var refused = await Streamed.Open(ada.Client, EventsController.Path))
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, refused.Response.StatusCode);
                Assert.Equal("application/problem+json", refused.Response.Content.Headers.ContentType?.MediaType);
            }

            // A visitor is not held to Ada's streams.
            await using (var visitor = await Streamed.Open(Agent(tangentSpace), EventsController.Path))
                Assert.Equal(HttpStatusCode.OK, visitor.Response.StatusCode);

            await held[0].DisposeAsync();
            held.RemoveAt(0);
            using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var again = await Streamed.Open(ada.Client, EventsController.Path);
                if (again.Response.StatusCode == HttpStatusCode.OK)
                {
                    held.Add(again);
                    break;
                }

                await again.DisposeAsync();
                await Task.Delay(100, bound.Token);
            }
        }
        finally
        {
            foreach (var stream in held) await stream.DisposeAsync();
        }
    }

    /// <summary>Waits, bounded, until no stream is open.</summary>
    private static async Task Released(EventStream streams)
    {
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (streams.Count("").InAll > 0) await Task.Delay(50, bound.Token);
    }

    private static async Task<Event> Created(Post post)
        => (await Event.All(Cancellation)).Single(e => e.PostId == post.Id && e.Kind == EventKind.PostCreated);
}
