using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Writer;
using Xunit;

namespace Tangent.Tangents;

/// <summary>The Tangent page both ways: the Tangent, its <c>can</c> and one bounded page of its Topics, pinned first
/// and then by last activity, with what is new to a signed-in reader marked; the gate for a reader who may not read;
/// the empty state and the start affordance; and the page's <c>ETag</c>.</summary>
public sealed class TangentPageTests
{
    private const string BaseUrl = TangentSpaceFixture.DefaultBaseUrl;
    private const string Workshop = "the-workshop";
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
    public async Task The_page_as_html_and_json_carries_the_tangent_its_can_and_its_topics_pinned_first_then_by_last_activity_and_a_reply_moves_its_topic_to_the_top()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Place(tangentSpace);
        var pinned = await Started(writer, "Read this first", Start);
        var older = await Started(writer, "An older question", Start.AddMinutes(1));
        var newer = await Started(writer, "A newer question", Start.AddMinutes(2));
        pinned.Pinned = true;
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(pinned), Cancellation));

        using (var json = await Json(tangentSpace.Client, "/t/" + Workshop))
        {
            var root = json.RootElement;
            Assert.Equal((Workshop, "The workshop", "Where things get made.", "topic", "public"),
                (root.GetProperty("slug").GetString(), root.GetProperty("name").GetString(), root.GetProperty("description").GetString(),
                    root.GetProperty("kind").GetString(), root.GetProperty("audience").GetString()));
            Assert.Equal($"{BaseUrl}/t/{Workshop}", root.GetProperty("address").GetString());
            Assert.Equal(["View"], Strings(root.GetProperty("can")));
            Assert.Equal([pinned.Id, newer.Id, older.Id], Ids(root));
            var first = root.GetProperty("topics")[0];
            Assert.Equal(("read-this-first", "Read this first", "topic", true), (first.GetProperty("slug").GetString(),
                first.GetProperty("title").GetString(), first.GetProperty("kind").GetString(), first.GetProperty("pinned").GetBoolean()));
            Assert.Equal($"{BaseUrl}/t/{Workshop}/read-this-first", first.GetProperty("address").GetString());
            Assert.Equal(("ada", "ada.example"), (first.GetProperty("author").GetProperty("id").GetString(), first.GetProperty("author").GetProperty("handle").GetString()));
            Assert.Equal("The words of the opening Post.", first.GetProperty("opening").GetString());
            Assert.False(root.TryGetProperty("next", out _));
        }

        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(Tids.Mint(), older.Id, "A reply.", "ada", At: Start.AddMinutes(3)), Cancellation));

        using (var json = await Json(tangentSpace.Client, "/t/" + Workshop))
        {
            Assert.Equal([pinned.Id, older.Id, newer.Id], Ids(json.RootElement));
            Assert.Equal(1, json.RootElement.GetProperty("topics")[1].GetProperty("replyCount").GetInt32());
        }

        var html = await Html(tangentSpace.Client, "/t/" + Workshop);
        Assert.Contains("<h1 id=\"tangent-title\" class=\"tangent-title\">The workshop</h1>", html);
        Assert.Contains("<summary class=\"switcher-button\" aria-current=\"page\" aria-label=\"The workshop, switch Tangent\">", html);
        Assert.Contains($"href=\"{BaseUrl}/t/{Workshop}/an-older-question\"", html);
        var order = new[] { "Read this first", "An older question", "A newer question" }.Select(title => html.IndexOf(title, StringComparison.Ordinal)).ToList();
        Assert.All(order, index => Assert.True(index > 0));
        Assert.Equal(order.Order().ToList(), order);
        Assert.Contains("<span class=\"topic-flag\">Pinned</span>", html);
        Assert.DoesNotContain("<dialog", html);
    }

    [Fact]
    public async Task More_topics_than_one_page_yield_a_cursor_and_the_next_page_continues_without_overlap_or_gap_and_no_answer_carries_a_count()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Place(tangentSpace);
        var all = new List<Post>();
        // Three Topics share every instant, so the order rests on the Id where last activity is the same; three are
        // pinned, so a page crosses from the pinned Topics to the rest.
        for (var index = 0; index < 2 * TopicList.PageSize + 1; index++)
            all.Add(await Started(writer, "Topic " + index, Start.AddMinutes(index / 3)));
        foreach (var topic in all.Take(3))
        {
            topic.Pinned = true;
            Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(topic), Cancellation));
        }

        var expected = all.Take(3).OrderByDescending(topic => topic.LastActivityAt).ThenByDescending(topic => topic.Id, StringComparer.Ordinal)
            .Concat(all.Skip(3).OrderByDescending(topic => topic.LastActivityAt).ThenByDescending(topic => topic.Id, StringComparer.Ordinal))
            .Select(topic => topic.Id).ToList();
        var read = new List<string>();
        var pages = new List<int>();
        string? address = "/t/" + Workshop;
        while (address is not null)
        {
            using var json = await Json(tangentSpace.Client, address);
            var root = json.RootElement;
            Assert.DoesNotContain(root.EnumerateObject(), property =>
                property.Name.Contains("count", StringComparison.OrdinalIgnoreCase) || property.Name.Contains("total", StringComparison.OrdinalIgnoreCase));
            var ids = Ids(root);
            pages.Add(ids.Count);
            read.AddRange(ids);
            address = root.TryGetProperty("next", out var next) ? new Uri(next.GetString()!).PathAndQuery : null;
        }

        Assert.Equal([TopicList.PageSize, TopicList.PageSize, 1], pages);
        Assert.Equal(expected, read);

        using var refused = await tangentSpace.Client.GetAsync($"/t/{Workshop}?after=not-a-cursor", Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Fact]
    public async Task A_signed_in_reader_sees_unseen_topics_marked_a_checkpoint_at_the_last_sequence_unmarks_one_a_later_reply_marks_it_again_and_a_visitor_sees_no_marks()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        var seen = await Started(writer, "Seen already", Start);
        var unseen = await Started(writer, "Never opened", Start.AddMinutes(1));
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(Tids.Mint(), seen.Id, "First reply.", "ada", At: Start.AddMinutes(2)), Cancellation));
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation)).Dispose();

        using (var json = await Json(browser.Client, "/t/" + Workshop))
        {
            Assert.All(json.RootElement.GetProperty("topics").EnumerateArray(), topic => Assert.True(topic.GetProperty("new").GetBoolean()));
            Assert.Equal(["View", "Post", "Topic"], Strings(json.RootElement.GetProperty("can")));
        }

        var root = await Post.Get(seen.Id, Cancellation);
        Assert.Equal(1, root!.LastSequence);
        Assert.IsType<Outcome<Checkpoint>.Done>(await writer.Send(
            new Store<Checkpoint>(Checkpoint.At(Reader, Scope.Topic(seen.Id), root.LastSequence)), Cancellation));

        using (var json = await Json(browser.Client, "/t/" + Workshop))
        {
            var marks = Marks(json.RootElement);
            Assert.Equal((false, 0L), marks[seen.Id]);
            Assert.Equal((true, 1L), marks[unseen.Id]);
        }

        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(Tids.Mint(), seen.Id, "Second reply.", "ada", At: Start.AddMinutes(3)), Cancellation));

        using (var json = await Json(browser.Client, "/t/" + Workshop))
            Assert.Equal((true, 1L), Marks(json.RootElement)[seen.Id]);
        var html = await Html(browser.Client, "/t/" + Workshop);
        Assert.Contains("<span class=\"new-dot\" role=\"img\" aria-label=\"Unread\"></span>", html);
        Assert.Contains("<span class=\"new-label\">1 new</span>", html);
        // The reader's account opens in the page, with the way to sign out and come back here.
        Assert.Contains("<details class=\"account\">", html);
        Assert.Contains($"<a class=\"account-item\" href=\"{BaseUrl}/auth/logout?return={Uri.EscapeDataString("/t/" + Workshop)}\">", html);
        Assert.Contains("</svg>Sign out</a>", html);

        using (var json = await Json(tangentSpace.Client, "/t/" + Workshop))
            Assert.All(Marks(json.RootElement).Values, mark => Assert.Equal((false, 0L), mark));
        var visitorHtml = await Html(tangentSpace.Client, "/t/" + Workshop);
        Assert.DoesNotContain("new-dot", visitorHtml);
        Assert.DoesNotContain("class=\"account\"", visitorHtml);
    }

    [Fact]
    public async Task A_segment_that_cannot_be_a_tangents_slug_answers_absent_on_the_tangent_page_and_the_topic_page()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        var segment = new string('a', 300);

        var tangent = await Problem(tangentSpace.Client, "/t/" + segment);
        var topic = await Problem(tangentSpace.Client, "/t/" + segment + "/some-topic");

        Assert.Equal((HttpStatusCode.NotFound, Capabilities.Absent), (tangent.Status, tangent.Gate));
        Assert.Equal(tangent, topic);
        Assert.Equal(await Problem(tangentSpace.Client, "/t/nowhere"), tangent);
    }

    [Fact]
    public async Task A_visitor_on_a_private_place_receives_sign_in_and_a_reader_the_tangent_is_absent_to_receives_what_a_missing_tangent_receives()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = await Place(tangentSpace);
        Assert.IsType<Outcome<Tangent>.Done>(await writer.Send(
            new CreateTangent("studio", "The studio", "", PostKind.Topic, 2, Audience.Private, "leo"), Cancellation));
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation)).Dispose();

        // A member of the place, where the Tangent's everyone overwrite denies View: the word 1.3 defines, absent,
        // in the answer an address without a Tangent gets.
        var absent = await Problem(browser.Client, "/t/studio");
        var missing = await Problem(browser.Client, "/t/nowhere");
        Assert.Equal((HttpStatusCode.NotFound, "application/problem+json", Capabilities.Absent), (absent.Status, absent.MediaType, absent.Gate));
        Assert.Equal(missing, absent);
        // A visitor on the private Tangent of a public place: absent too, by the order access resolves in.
        Assert.Equal(missing, await Problem(tangentSpace.Client, "/t/studio"));

        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Audience = Audience.Private;
        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Store<TangentSpace>(space), Cancellation));

        var visitor = await Problem(tangentSpace.Client, "/t/" + Workshop);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn), (visitor.Status, visitor.Gate));
        Assert.Equal(visitor, await Problem(tangentSpace.Client, "/t/nowhere"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/t/" + Workshop);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var page = await tangentSpace.Client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.Unauthorized, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync(Cancellation);
        Assert.Contains("<p data-gate=\"sign-in\">sign-in</p>", html);
        Assert.DoesNotContain("The workshop", html);
    }

    [Fact]
    public async Task A_tangent_without_topics_renders_its_empty_state_and_the_start_affordance_shows_only_where_can_holds_topic()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "did:plc:newcomer", [], ct: Cancellation);
        using var member = new Browser(tangentSpace);
        (await member.SignIn("reader", Reader, Cancellation)).Dispose();
        using var newcomer = new Browser(tangentSpace);
        (await newcomer.SignIn("newcomer", "did:plc:newcomer", Cancellation)).Dispose();
        var start = $"{BaseUrl}/t/{Workshop}/new";

        var visitorHtml = await Html(tangentSpace.Client, "/t/" + Workshop);
        Assert.Contains("<p class=\"topic-empty-title\">No topics yet.</p>", visitorHtml);
        Assert.DoesNotContain("tangent-new", visitorHtml);
        Assert.Contains($"href=\"{BaseUrl}/sign-in?return={Uri.EscapeDataString("/t/" + Workshop)}\"", visitorHtml);
        using (var json = await Json(tangentSpace.Client, "/t/" + Workshop))
        {
            Assert.Empty(json.RootElement.GetProperty("topics").EnumerateArray());
            Assert.False(json.RootElement.TryGetProperty("newTopic", out _));
        }

        var newcomerHtml = await Html(newcomer.Client, "/t/" + Workshop);
        Assert.Contains("No topics yet.", newcomerHtml);
        Assert.DoesNotContain("tangent-new", newcomerHtml);
        Assert.DoesNotContain("/sign-in?return=", newcomerHtml);

        var memberHtml = await Html(member.Client, "/t/" + Workshop);
        Assert.Contains("No topics yet.", memberHtml);
        Assert.Contains($"<a class=\"tangent-new\" href=\"{start}\">", memberHtml);
        using (var json = await Json(member.Client, "/t/" + Workshop))
            Assert.Equal(start, json.RootElement.GetProperty("newTopic").GetString());
    }

    [Fact]
    public async Task The_same_page_answers_the_same_etag_a_conditional_request_answers_304_and_a_reply_changes_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Place(tangentSpace);
        var topic = await Started(writer, "A question", Start);

        var first = await Tag(tangentSpace.Client, null);
        var second = await Tag(tangentSpace.Client, null);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(first.ETag, second.ETag);
        Assert.Equal(HttpStatusCode.NotModified, (await Tag(tangentSpace.Client, first.ETag)).Status);

        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(Tids.Mint(), topic.Id, "An answer.", "ada", At: Start.AddMinutes(1)), Cancellation));

        var after = await Tag(tangentSpace.Client, first.ETag);
        Assert.Equal(HttpStatusCode.OK, after.Status);
        Assert.NotEqual(first.ETag, after.ETag);
    }

    [Fact]
    public async Task A_row_carries_its_latest_reply_and_recent_authors_and_the_frame_lists_the_tangents_the_reader_may_view_and_who_reads()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = await Place(tangentSpace);
        Assert.IsType<Outcome<Tangent>.Done>(await writer.Send(
            new CreateTangent("studio", "The studio", "", PostKind.Article, 2, Audience.Private, "leo"), Cancellation));
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        var topic = await Started(writer, "A question", Start);
        var reply = Tids.Mint();
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(Tids.Mint(), topic.Id, "First.", "leo", At: Start.AddMinutes(1)), Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(reply, topic.Id, "The **latest** word.", "ada", At: Start.AddMinutes(2)), Cancellation));

        using (var json = await Json(tangentSpace.Client, "/t/" + Workshop))
        {
            var row = json.RootElement.GetProperty("topics")[0];
            var latest = row.GetProperty("latest");
            Assert.Equal((reply, "ada", "The latest word.", false), (latest.GetProperty("id").GetString(),
                latest.GetProperty("author").GetProperty("id").GetString(), latest.GetProperty("line").GetString(), latest.GetProperty("new").GetBoolean()));
            Assert.Equal(["ada", "leo"], row.GetProperty("people").EnumerateArray().Select(person => person.GetProperty("id").GetString()!).ToList());
            Assert.Equal(1, row.GetProperty("readMinutes").GetInt32());
            var tangents = json.RootElement.GetProperty("tangents").EnumerateArray().ToList();
            Assert.Equal([(Workshop, 1)], tangents.Select(tangent => (tangent.GetProperty("slug").GetString()!, tangent.GetProperty("topicCount").GetInt32())).ToList());
            Assert.Equal($"{BaseUrl}/sign-in?return={Uri.EscapeDataString("/t/" + Workshop)}", json.RootElement.GetProperty("signIn").GetString());
        }

        var visitorHtml = await Html(tangentSpace.Client, "/t/" + Workshop);
        Assert.Contains($"<a class=\"masthead-sign-in\" href=\"{BaseUrl}/sign-in?return={Uri.EscapeDataString("/t/" + Workshop)}\">Sign in</a>", visitorHtml);
        Assert.Contains("<span class=\"switcher-item-detail\">Discussions · 1 topic</span>", WebUtility.HtmlDecode(visitorHtml));
        Assert.DoesNotContain("The studio", visitorHtml);
        Assert.Contains("<span class=\"topic-row-latest-line\"><b>ada.example</b> The latest word.</span>", visitorHtml);

        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation)).Dispose();
        var readerHtml = await Html(browser.Client, "/t/" + Workshop);
        Assert.DoesNotContain("masthead-sign-in", readerHtml);
        Assert.Contains(" · Member</span>", WebUtility.HtmlDecode(readerHtml));
        Assert.Contains($"<li><a class=\"account-item\" href=\"{BaseUrl}/auth/logout?return={Uri.EscapeDataString("/t/" + Workshop)}\">", readerHtml);
        Assert.Contains("data-theme-choice=\"dark\"", readerHtml[..readerHtml.IndexOf("account-items", StringComparison.Ordinal)]);
        Assert.DoesNotContain("The studio", readerHtml);
        Assert.Contains("<span class=\"new-label new-beside\">New</span>", readerHtml);
    }

    [Fact]
    public void The_version_differs_across_midnight_utc_for_the_same_page_since_the_day_changes_how_its_times_read()
    {
        var at = new DateTimeOffset(2026, 10, 29, 15, 0, 0, TimeSpan.Zero);
        var author = new TangentPage.Author("ada", "ada.example", "Ada", "person", null);
        var topic = new TangentPage.Topic(Tids.Mint(), "a-question", "A question", "topic", $"{BaseUrl}/t/{Workshop}/a-question", author,
            "Words.", 1, 0, at, at, false, false, null, [author], false, 0);
        var today = new TangentPage(Workshop, "The workshop", "", "topic", "public", $"{BaseUrl}/t/{Workshop}", ["View"], [topic], null,
            false, null, null, null, [], "Tangent Space", BaseUrl + "/", null, new DateOnly(2026, 10, 29));
        var tomorrow = today with { Day = new DateOnly(2026, 10, 30) };

        Assert.Equal(TangentPage.Version(today), TangentPage.Version(today with { }));
        Assert.NotEqual(TangentPage.Version(today), TangentPage.Version(tomorrow));
        Assert.Equal("3:00 PM", Render.TopicRow.When(at, today.Day));
        Assert.Equal("Oct 29", Render.TopicRow.When(at, tomorrow.Day));
        Assert.Equal("Oct 29, 2026", Render.TopicRow.When(at, new DateOnly(2027, 1, 1)));
    }

    /// <summary>The place every proof starts from: established, with an administrator who sets up the public Tangent,
    /// and a member who starts its Topics.</summary>
    private static async Task<WriteLoop> Place(TangentSpaceFixture tangentSpace)
    {
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ada", [Roles.Member], ct: Cancellation);
        Assert.IsType<Outcome<Tangent>.Done>(await writer.Send(
            new CreateTangent(Workshop, "The workshop", "Where things get made.", PostKind.Topic, 1, Audience.Public, "leo"), Cancellation));
        return writer;
    }

    private static async Task<Post> Started(WriteLoop writer, string title, DateTimeOffset at)
        => Assert.IsType<Outcome<Post>.Done>(await writer.Send(
            new StartTopic(Tids.Mint(), Workshop, title, "The *words* of the [opening](https://example.com) Post.", "ada", At: at), Cancellation)).Result;

    private static List<string> Ids(JsonElement page)
        => page.GetProperty("topics").EnumerateArray().Select(topic => topic.GetProperty("id").GetString()!).ToList();

    private static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToList();

    private static Dictionary<string, (bool New, long NewPosts)> Marks(JsonElement page)
        => page.GetProperty("topics").EnumerateArray().ToDictionary(topic => topic.GetProperty("id").GetString()!,
            topic => (topic.GetProperty("new").GetBoolean(), topic.GetProperty("newPosts").GetInt64()));

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

    private static async Task<(HttpStatusCode Status, EntityTagHeaderValue? ETag)> Tag(HttpClient client, EntityTagHeaderValue? held)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/t/" + Workshop);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (held is not null) request.Headers.IfNoneMatch.Add(held);
        using var response = await client.SendAsync(request, Cancellation);
        return (response.StatusCode, response.Headers.ETag);
    }
}
