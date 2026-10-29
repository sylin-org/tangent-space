using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;
using Xunit;

namespace Tangent.Topics;

/// <summary>The Topic both ways: its root and one bounded window of its replies, each Post with its author and
/// <c>can</c>; windows whose cursors are sequence numbers and cover every reply once; the window opened around a Post's
/// address, at a reader's first unread or from the start; the byte budget; and what a reader who may not read, a
/// removed Post and the page's <c>ETag</c> receive. Every Topic and reply is written through the writer's commands.</summary>
public sealed class TopicWindowTests(ITestOutputHelper output)
{
    private const string BaseUrl = TangentSpaceFixture.DefaultBaseUrl;
    private const string Workshop = "the-workshop";
    private const string Leo = "did:plc:leo";
    private const string Ada = "did:plc:ada";
    private const string Reader = "did:plc:reader";

    private static readonly DateTimeOffset Start = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A Tangent Space where Koan's test connector signs people in, and no profile job reaches the network.</summary>
    private static readonly Dictionary<string, string?> TestConnector = new()
    {
        ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
        ["Tangent:Profile:BaseAddress"] = "http://127.0.0.1:1/",
        ["Tangent:Profile:Timeout"] = "00:00:01"
    };

    [Fact]
    public async Task A_topic_as_html_and_json_carries_its_root_and_its_replies_in_sequence_order_each_with_its_author_and_can()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = await Place(tangentSpace);
        var topic = await Started(writer, "An opening question", "First line.\nSecond <b>line</b>.");
        var first = await Replied(writer, topic, "Agreed.", Leo, 1);
        var second = await Replied(writer, topic, "Thanks, Leo.", Ada, 2, parentId: first.Id);
        var third = await Replied(writer, topic, "And one more.", Leo, 3);
        var address = $"{BaseUrl}/t/{Workshop}/an-opening-question";

        using (var json = await Json(tangentSpace.Client, "/t/" + Workshop + "/an-opening-question"))
        {
            var page = json.RootElement;
            Assert.Equal((topic.Id, "an-opening-question", "An opening question", "topic", address),
                (page.GetProperty("id").GetString(), page.GetProperty("slug").GetString(), page.GetProperty("title").GetString(),
                    page.GetProperty("kind").GetString(), page.GetProperty("address").GetString()));
            Assert.Equal(["View"], Strings(page.GetProperty("can")));
            Assert.Equal(Capabilities.SignIn, page.GetProperty("gate").GetString());
            var root = page.GetProperty("root");
            Assert.Equal((topic.Id, 0L, Ada, "First line.\nSecond <b>line</b>."), (root.GetProperty("id").GetString(), root.GetProperty("sequence").GetInt64(),
                root.GetProperty("author").GetProperty("id").GetString(), root.GetProperty("body").GetString()));
            Assert.Equal(Start, root.GetProperty("createdAt").GetDateTimeOffset());
            var replies = page.GetProperty("replies").EnumerateArray().ToList();
            Assert.Equal([(first.Id, 1L, Leo), (second.Id, 2L, Ada), (third.Id, 3L, Leo)], replies.Select(reply =>
                (reply.GetProperty("id").GetString()!, reply.GetProperty("sequence").GetInt64(), reply.GetProperty("author").GetProperty("id").GetString()!)).ToList());
            Assert.Equal("did:plc:leo.example", replies[0].GetProperty("author").GetProperty("handle").GetString());
            Assert.All(replies.Prepend(root), post => Assert.Equal(["View"], Strings(post.GetProperty("can"))));
            Assert.All(replies.Prepend(root), post => Assert.False(post.TryGetProperty("edit", out _)));
            Assert.Equal($"{address}/{second.Id}", replies[1].GetProperty("address").GetString());
            Assert.Equal((first.Id, $"{address}/{first.Id}"), (replies[1].GetProperty("parentId").GetString(), replies[1].GetProperty("parent").GetString()));
            Assert.False(replies[0].TryGetProperty("parent", out _));
            Assert.Equal((1L, 3L), (page.GetProperty("from").GetInt64(), page.GetProperty("through").GetInt64()));
            Assert.False(page.TryGetProperty("older", out _));
            Assert.False(page.TryGetProperty("newer", out _));
        }

        var visitorHtml = await Html(tangentSpace.Client, "/t/" + Workshop + "/an-opening-question");
        Assert.Contains("<h1 class=\"topic-title\">An opening question</h1>", visitorHtml);
        Assert.Contains("First line.<br>Second &lt;b&gt;line&lt;/b&gt;.", visitorHtml);
        var order = new[] { "First line.", "Agreed.", "Thanks, Leo.", "And one more." }.Select(text => visitorHtml.IndexOf(text, StringComparison.Ordinal)).ToList();
        Assert.All(order, index => Assert.True(index > 0));
        Assert.Equal(order.Order().ToList(), order);
        Assert.Contains($"<a class=\"post-parent\" href=\"{address}/{first.Id}\">In reply to did:plc:leo.example</a>", visitorHtml);
        Assert.DoesNotContain("post-action", visitorHtml);
        Assert.DoesNotContain("<dialog", visitorHtml);

        // Ada reads her own Posts as hers: she may edit them, and the page links each one to its edit address; Leo's
        // she may only read and reply to.
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("ada", Ada, Cancellation)).Dispose();
        using (var json = await Json(browser.Client, "/t/" + Workshop + "/an-opening-question"))
        {
            var page = json.RootElement;
            Assert.Contains("Post", Strings(page.GetProperty("can")));
            Assert.False(page.TryGetProperty("gate", out _));
            var replies = page.GetProperty("replies").EnumerateArray().ToList();
            Assert.Contains("EditOwn", Strings(page.GetProperty("root").GetProperty("can")));
            Assert.Equal($"{address}/{topic.Id}/edit", page.GetProperty("root").GetProperty("edit").GetString());
            Assert.Equal($"{address}/{second.Id}/edit", replies[1].GetProperty("edit").GetString());
            Assert.Equal(["View", "Post", "Topic"], Strings(replies[0].GetProperty("can")));
            Assert.False(replies[0].TryGetProperty("edit", out _));
        }

        var adaHtml = await Html(browser.Client, "/t/" + Workshop + "/an-opening-question");
        Assert.Contains($"<a class=\"post-action\" href=\"{address}/{second.Id}/edit\">Edit</a>", adaHtml);
        Assert.DoesNotContain($"{address}/{first.Id}/edit", adaHtml);
    }

    [Fact]
    public async Task More_replies_than_one_window_yield_cursors_that_cover_every_reply_once_both_ways_with_no_total_through_the_window_index_with_a_limit()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Place(tangentSpace);
        var topic = await Started(writer, "A long one", "Many replies follow.");
        var count = 2 * WindowPlanner.Limit + 3;
        for (var index = 1; index <= count; index++) await Replied(writer, topic, "Reply " + index + ".", index % 2 == 0 ? Leo : Ada, index);
        var expected = Enumerable.Range(1, count).Select(index => (long)index).ToList();

        var forward = new List<long>();
        var sizes = new List<int>();
        string? address = "/t/" + Workshop + "/a-long-one";
        string? last = null;
        while (address is not null)
        {
            using var json = await Json(tangentSpace.Client, address);
            var page = json.RootElement;
            Assert.DoesNotContain(Names(page), name => name.Contains("total", StringComparison.OrdinalIgnoreCase));
            Assert.All(Names(page).Where(name => name.Contains("count", StringComparison.OrdinalIgnoreCase)), name => Assert.Equal("replyCount", name));
            var sequences = Sequences(page);
            Assert.Equal((sequences[0], sequences[^1]), (page.GetProperty("from").GetInt64(), page.GetProperty("through").GetInt64()));
            sizes.Add(sequences.Count);
            forward.AddRange(sequences);
            last = address;
            address = page.TryGetProperty("newer", out var newer) ? new Uri(newer.GetString()!).PathAndQuery : null;
        }

        Assert.Equal([WindowPlanner.Limit, WindowPlanner.Limit, 3], sizes);
        Assert.Equal(expected, forward);

        var backward = new List<List<long>>();
        address = last;
        while (address is not null)
        {
            using var json = await Json(tangentSpace.Client, address);
            backward.Add(Sequences(json.RootElement));
            address = json.RootElement.TryGetProperty("older", out var older) ? new Uri(older.GetString()!).PathAndQuery : null;
        }

        Assert.Equal(expected, Enumerable.Reverse(backward).SelectMany(window => window).ToList());

        using (var refused = await tangentSpace.Client.GetAsync($"/t/{Workshop}/a-long-one?after=one", Cancellation))
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using (var refused = await tangentSpace.Client.GetAsync($"/t/{Workshop}/a-long-one?after=1&before=9", Cancellation))
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var plans = Plans(tangentSpace.DatabasePath, topic.Id);
        Assert.All(plans, plan => Assert.Contains(" LIMIT ", plan.Sql, StringComparison.Ordinal));
        Assert.All(plans, plan => Assert.Contains(plan.Plan, detail => detail.StartsWith("SEARCH", StringComparison.Ordinal)
            && detail.Contains("USING INDEX ix_post_windows", StringComparison.Ordinal)));
        Assert.All(plans, plan => Assert.DoesNotContain(plan.Plan, detail => detail.Contains("TEMP B-TREE", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_posts_address_opens_the_window_around_it_marked_and_a_post_of_another_topic_answers_as_absent()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Place(tangentSpace);
        var topic = await Started(writer, "Around a reply", "The opening.");
        var other = await Started(writer, "Another topic", "Somewhere else.");
        var elsewhere = await Replied(writer, other, "A reply elsewhere.", Leo, 1);
        var replies = new List<Post>();
        for (var index = 1; index <= 40; index++) replies.Add(await Replied(writer, topic, "Reply " + index + ".", Leo, index));
        var anchor = replies[29];

        using (var json = await Json(tangentSpace.Client, $"/t/{Workshop}/around-a-reply/{anchor.Id}"))
        {
            var page = json.RootElement;
            var sequences = Sequences(page);
            Assert.Equal(anchor.Id, page.GetProperty("anchor").GetString());
            Assert.Contains(30L, sequences);
            Assert.Equal(WindowPlanner.Limit, sequences.Count);
            Assert.Equal(Enumerable.Range((int)sequences[0], sequences.Count).Select(index => (long)index).ToList(), sequences);
            Assert.True(page.TryGetProperty("older", out _));
            Assert.Equal(40L, page.GetProperty("through").GetInt64());
        }

        var html = await Html(tangentSpace.Client, $"/t/{Workshop}/around-a-reply/{anchor.Id}");
        Assert.Contains($"<article id=\"{anchor.Id}\" class=\"post is-anchor\" aria-current=\"true\">", html);
        Assert.Single(Regex.Matches(html, "<article [^>]*aria-current=\"true\""));

        var missing = await Problem(tangentSpace.Client, $"/t/{Workshop}/nothing-here");
        Assert.Equal((HttpStatusCode.NotFound, Capabilities.Absent), (missing.Status, missing.Gate));
        Assert.Equal(missing, await Problem(tangentSpace.Client, $"/t/{Workshop}/around-a-reply/{elsewhere.Id}"));
        Assert.Equal(missing, await Problem(tangentSpace.Client, $"/t/{Workshop}/around-a-reply/{Tids.Mint()}"));
        Assert.Equal(missing, await Problem(tangentSpace.Client, $"/t/{Workshop}/around-a-reply/not-a-tid"));
    }

    [Fact]
    public async Task A_signed_in_reader_with_a_checkpoint_opens_at_their_first_unread_reading_moves_nothing_and_a_visitor_opens_at_the_start()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        var topic = await Started(writer, "Where it opens", "The opening.");
        for (var index = 1; index <= 40; index++) await Replied(writer, topic, "Reply " + index + ".", Leo, index);
        Assert.IsType<Outcome<Checkpoint>.Done>(await writer.Send(new Store<Checkpoint>(Checkpoint.At(Reader, Scope.Topic(topic.Id), 30)), Cancellation));
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation)).Dispose();

        using (var json = await Json(browser.Client, $"/t/{Workshop}/where-it-opens"))
        {
            var page = json.RootElement;
            Assert.Equal(Enumerable.Range(31, 10).Select(index => (long)index).ToList(), Sequences(page));
            Assert.Equal($"{BaseUrl}/t/{Workshop}/where-it-opens?before=31", page.GetProperty("older").GetString());
            Assert.False(page.TryGetProperty("newer", out _));
            Assert.All(page.GetProperty("replies").EnumerateArray(), reply => Assert.True(reply.GetProperty("new").GetBoolean()));
            Assert.False(page.GetProperty("root").GetProperty("new").GetBoolean());
        }

        Assert.Equal(30, (await Checkpoint.Get(Checkpoint.Key(Reader, Scope.Topic(topic.Id)), Cancellation))!.Seq);

        using (var json = await Json(tangentSpace.Client, $"/t/{Workshop}/where-it-opens"))
        {
            Assert.Equal(Enumerable.Range(1, WindowPlanner.Limit).Select(index => (long)index).ToList(), Sequences(json.RootElement));
            Assert.False(json.RootElement.TryGetProperty("older", out _));
            Assert.All(json.RootElement.GetProperty("replies").EnumerateArray(), reply => Assert.False(reply.GetProperty("new").GetBoolean()));
        }

        // A reader who has read everything opens at the newest window.
        Assert.IsType<Outcome<Checkpoint>.Done>(await writer.Send(new Store<Checkpoint>(Checkpoint.At(Reader, Scope.Topic(topic.Id), 40)), Cancellation));
        using (var json = await Json(browser.Client, $"/t/{Workshop}/where-it-opens"))
            Assert.Equal(Enumerable.Range(40 - WindowPlanner.Limit + 1, WindowPlanner.Limit).Select(index => (long)index).ToList(), Sequences(json.RootElement));
    }

    [Fact]
    public async Task The_byte_budget_cuts_a_window_of_very_long_posts_short_and_the_cursor_continues_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Place(tangentSpace);
        var topic = await Started(writer, "Long posts", "Each reply is nearly as long as a Post may be.");
        var body = string.Join(' ', Enumerable.Repeat("words", 60 * 1024 / 6));
        var count = 20;
        for (var index = 1; index <= count; index++) await Replied(writer, topic, index + " " + body, Leo, index);

        var windows = new List<List<long>>();
        string? address = $"/t/{Workshop}/long-posts";
        while (address is not null)
        {
            using var json = await Json(tangentSpace.Client, address);
            var replies = json.RootElement.GetProperty("replies").EnumerateArray().ToList();
            var bytes = replies.Sum(reply => Encoding.UTF8.GetByteCount(reply.GetProperty("body").GetString()!));
            Assert.True(bytes <= WindowPlanner.ResultBudget - WindowPlanner.EnvelopeReserve, $"A window carried {bytes} bytes of bodies.");
            windows.Add(Sequences(json.RootElement));
            address = json.RootElement.TryGetProperty("newer", out var newer) ? new Uri(newer.GetString()!).PathAndQuery : null;
        }

        output.WriteLine("Windows: " + string.Join(" | ", windows.Select(window => window.Count)));
        Assert.True(windows[0].Count < count, "The first window held every reply.");
        Assert.True(windows.Count > 1);
        Assert.Equal(Enumerable.Range(1, count).Select(index => (long)index).ToList(), windows.SelectMany(window => window).ToList());
    }

    [Fact]
    public async Task A_reader_who_may_not_view_gets_the_absent_answer_a_tombstone_keeps_its_place_and_the_etag_holds_until_a_reply()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(
            new CreateTangent("studio", "The studio", "", PostKind.Topic, 2, Audience.Private, Leo), Cancellation));
        var hidden = Assert.IsType<Outcome<Post>.Done>(await writer.Send(
            new StartTopic(Tids.Mint(), "studio", "Behind the door", "Not for everyone.", Leo, At: Start), Cancellation)).Result;
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation)).Dispose();

        var missing = await Problem(browser.Client, "/t/studio/nothing-here");
        Assert.Equal((HttpStatusCode.NotFound, Capabilities.Absent), (missing.Status, missing.Gate));
        Assert.Equal(missing, await Problem(browser.Client, "/t/studio/behind-the-door"));
        Assert.Equal(missing, await Problem(browser.Client, $"/t/studio/behind-the-door/{hidden.Id}"));
        Assert.Equal(missing, await Problem(tangentSpace.Client, "/t/studio/behind-the-door"));
        Assert.Equal(missing, await Problem(browser.Client, "/t/nowhere/behind-the-door"));

        var topic = await Started(writer, "A removal", "The opening.");
        await Replied(writer, topic, "Kept.", Ada, 1);
        var removed = await Replied(writer, topic, "Said and taken back.", Ada, 2);
        await Replied(writer, topic, "Also kept.", Ada, 3);
        var row = (await Post.Get(removed.Id, Cancellation))!;
        row.Removed = true;
        row.RemovedBy = Leo;
        row.RemovedAt = Start.AddMinutes(10);
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(row), Cancellation));

        using (var json = await Json(tangentSpace.Client, $"/t/{Workshop}/a-removal"))
        {
            var replies = json.RootElement.GetProperty("replies").EnumerateArray().ToList();
            Assert.Equal([1L, 2L, 3L], Sequences(json.RootElement));
            var tombstone = replies[1];
            Assert.Equal((removed.Id, Ada, Leo), (tombstone.GetProperty("id").GetString(), tombstone.GetProperty("author").GetProperty("id").GetString(),
                tombstone.GetProperty("removed").GetProperty("by").GetProperty("id").GetString()));
            Assert.Equal(Start.AddMinutes(2), tombstone.GetProperty("createdAt").GetDateTimeOffset());
            Assert.False(tombstone.TryGetProperty("body", out _));
            Assert.Empty(tombstone.GetProperty("can").EnumerateArray());
        }

        var html = await Html(tangentSpace.Client, $"/t/{Workshop}/a-removal");
        Assert.Contains("Removed by did:plc:leo.example", html);
        Assert.DoesNotContain("Said and taken back.", html);
        Assert.True(html.IndexOf("Kept.", StringComparison.Ordinal) < html.IndexOf("Removed by", StringComparison.Ordinal)
            && html.IndexOf("Removed by", StringComparison.Ordinal) < html.IndexOf("Also kept.", StringComparison.Ordinal));

        var first = await Tag(tangentSpace.Client, $"/t/{Workshop}/a-removal", null);
        var second = await Tag(tangentSpace.Client, $"/t/{Workshop}/a-removal", null);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(first.ETag, second.ETag);
        Assert.Equal(HttpStatusCode.NotModified, (await Tag(tangentSpace.Client, $"/t/{Workshop}/a-removal", first.ETag)).Status);
        await Replied(writer, topic, "A late reply.", Leo, 4);
        var after = await Tag(tangentSpace.Client, $"/t/{Workshop}/a-removal", first.ETag);
        Assert.Equal(HttpStatusCode.OK, after.Status);
        Assert.NotEqual(first.ETag, after.ETag);

        // A locked Topic says so, and no one may reply in it.
        var root = (await Post.Get(topic.Id, Cancellation))!;
        root.Locked = true;
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(root), Cancellation));
        using (var json = await Json(browser.Client, $"/t/{Workshop}/a-removal"))
        {
            Assert.True(json.RootElement.GetProperty("locked").GetBoolean());
            Assert.DoesNotContain("Post", Strings(json.RootElement.GetProperty("can")));
            Assert.Equal(Capabilities.Locked, json.RootElement.GetProperty("gate").GetString());
        }

        Assert.Contains("This topic is locked; it takes no new replies.", await Html(browser.Client, $"/t/{Workshop}/a-removal"));
    }

    [Fact]
    public async Task In_a_locked_or_removed_topic_no_reply_holds_post_with_the_topics_gate_and_an_open_topics_replies_are_unchanged()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        var topic = await Started(writer, "A closing", "The opening.");
        var leos = await Replied(writer, topic, "Leo's reply.", Leo, 1);
        var readers = await Replied(writer, topic, "The reader's reply.", Reader, 2);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation)).Dispose();

        async Task<List<List<string>>> ReplyCans()
        {
            using var json = await Json(browser.Client, $"/t/{Workshop}/a-closing");
            return json.RootElement.GetProperty("replies").EnumerateArray().Select(reply => Strings(reply.GetProperty("can"))).ToList();
        }

        var open = await ReplyCans();
        Assert.Equal(["View", "Post", "Topic"], open[0]);
        Assert.Equal(["View", "Post", "Topic", "EditOwn", "RemoveOwn"], open[1]);

        // The gate each reply's Post carries, from the same pure function the page asks, over the reader's rows.
        var participant = (await Participant.Get(Reader, Cancellation))!;
        var member = Bits.Tokens(Roles.MemberBits).Append(Roles.Member).ToList();
        var place = new TangentSpace { Audience = Audience.Public };
        string? Gate(Post reply, Post root) => Capabilities.For(participant, member, place, [], DateTimeOffset.UtcNow, reply, root).Gate(Bit.Post);

        var root = (await Post.Get(topic.Id, Cancellation))!;
        root.Locked = true;
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(root), Cancellation));
        var locked = await ReplyCans();
        Assert.All(locked, can => Assert.DoesNotContain("Post", can));
        Assert.Equal(["View", "Topic", "EditOwn", "RemoveOwn"], locked[1]);
        Assert.All([leos, readers], reply => Assert.Equal(Capabilities.Locked, Gate(reply, root)));

        root.Locked = false;
        root.Removed = true;
        root.RemovedBy = Leo;
        root.RemovedAt = Start.AddMinutes(5);
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(root), Cancellation));
        var removed = await ReplyCans();
        Assert.All(removed, can => Assert.DoesNotContain("Post", can));
        Assert.Equal(["View", "Topic", "EditOwn", "RemoveOwn"], removed[1]);
        Assert.All([leos, readers], reply => Assert.Equal(Capabilities.Removed, Gate(reply, root)));

        root.Removed = false;
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(root), Cancellation));
        Assert.Equal(open, await ReplyCans());
        Assert.All([leos, readers], reply => Assert.Null(Gate(reply, root)));
    }

    /// <summary>The place every proof starts from: established, with an administrator who sets up the public Tangent,
    /// and a member who starts its Topics.</summary>
    private static async Task<WriteLoop> Place(TangentSpaceFixture tangentSpace)
    {
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(
            new CreateTangent(Workshop, "The workshop", "Where things get made.", PostKind.Topic, 1, Audience.Public, Leo), Cancellation));
        return writer;
    }

    private static async Task<Post> Started(WriteLoop writer, string title, string body)
        => Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(Tids.Mint(), Workshop, title, body, Ada, At: Start), Cancellation)).Result;

    private static async Task<Post> Replied(WriteLoop writer, Post topic, string body, string author, int minute, string? parentId = null)
        => Assert.IsType<Outcome<Post>.Done>(await writer.Send(
            new Reply(Tids.Mint(), topic.Id, body, author, parentId, Start.AddMinutes(minute)), Cancellation)).Result;

    private static List<long> Sequences(JsonElement page)
        => page.GetProperty("replies").EnumerateArray().Select(reply => reply.GetProperty("sequence").GetInt64()).ToList();

    private static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToList();

    /// <summary>Every property name in the document, at any depth.</summary>
    private static IEnumerable<string> Names(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Names(property.Value).Prepend(property.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Names),
        _ => []
    };

    private static async Task<JsonDocument> Json(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
    }

    private static async Task<string> Html(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var response = await client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Cancellation);
    }

    private sealed record Answer(HttpStatusCode Status, string? MediaType, string? Title, string? Detail, string? Gate);

    private static async Task<Answer> Problem(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, Cancellation);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        string? Text(string name) => json.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        return new Answer(response.StatusCode, response.Content.Headers.ContentType?.MediaType, Text("title"), Text("detail"), Text("gate"));
    }

    private static async Task<(HttpStatusCode Status, EntityTagHeaderValue? ETag)> Tag(HttpClient client, string path, EntityTagHeaderValue? held)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (held is not null) request.Headers.IfNoneMatch.Add(held);
        using var response = await client.SendAsync(request, Cancellation);
        return (response.StatusCode, response.Headers.ETag);
    }

    /// <summary>The two statements Koan's SQLite connector composes for the window's reads — the replies after a
    /// sequence through the boundary, oldest first, and the replies before one, newest first: the fields read as the
    /// dialect reads them, which are the expressions the window index is declared on, the order on the sequence, then
    /// LIMIT and OFFSET — and the plan SQLite gives each, as its detail lines.</summary>
    private List<(string Sql, List<string> Plan)> Plans(string database, string topicId)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        var (table, windows) = Index(connection, "ix_post_windows");
        var statements = new[]
        {
            $"SELECT * FROM \"{table}\" WHERE {windows[0]} = '{topicId}' AND {windows[1]} > 25 AND {windows[1]} <= 53 ORDER BY {windows[1]} LIMIT {WindowPlanner.Limit} OFFSET 0",
            $"SELECT * FROM \"{table}\" WHERE {windows[0]} = '{topicId}' AND {windows[1]} < 26 AND {windows[1]} > 0 ORDER BY {windows[1]} DESC LIMIT {WindowPlanner.Limit} OFFSET 0"
        };
        var plans = new List<(string, List<string>)>();
        foreach (var statement in statements)
        {
            using var explain = connection.CreateCommand();
            explain.CommandText = "EXPLAIN QUERY PLAN " + statement;
            using var reader = explain.ExecuteReader();
            var details = new List<string>();
            while (reader.Read()) details.Add(reader.GetString(3));
            output.WriteLine(statement);
            output.WriteLine("  " + string.Join(" | ", details));
            plans.Add((statement, details));
        }

        return plans;
    }

    /// <summary>The table an index is on and the expressions it is declared on, which are the dialect's reads of the
    /// fields without the row's alias.</summary>
    private static (string Table, List<string> Expressions) Index(SqliteConnection connection, string name)
    {
        using var find = connection.CreateCommand();
        find.CommandText = "SELECT tbl_name, sql FROM sqlite_master WHERE type = 'index' AND name = $name";
        find.Parameters.AddWithValue("$name", name);
        using var row = find.ExecuteReader();
        Assert.True(row.Read(), $"No index is named {name}.");
        var (table, sql) = (row.GetString(0), row.GetString(1));
        var list = sql[(sql.IndexOf(" (", sql.IndexOf(" ON ", StringComparison.Ordinal), StringComparison.Ordinal) + 2)..^1];
        var expressions = new List<string>();
        var (depth, quoted, from) = (0, '\0', 0);
        for (var index = 0; index < list.Length; index++)
        {
            var character = list[index];
            if (quoted != '\0') { if (character == quoted) quoted = '\0'; continue; }
            if (character is '\'' or '"') quoted = character;
            else if (character == '(') depth++;
            else if (character == ')') depth--;
            else if (character == ',' && depth == 0) { expressions.Add(list[from..index].Trim()); from = index + 1; }
        }

        expressions.Add(list[from..].Trim());
        return (table, expressions);
    }
}
