using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tangent.Access;
using Tangent.Capture;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tools;
using Xunit;

namespace Tangent.Tangents;

/// <summary>The Tangent page both ways: the Tangent, its <c>can</c> and one bounded page of its Topics, a discussions Tangent's
/// by last activity and an articles Tangent's by when each started, with what is new to a signed-in reader marked; the gate for a reader who may not read;
/// the empty state and the start affordance; a Topic whose root is removed, marked; an articles Tangent narrowed by month
/// and by author across all its articles; and the page's <c>ETag</c>.</summary>
public sealed class TangentPageTests
{
    private const string BaseUrl = TangentSpaceFixture.DefaultBaseUrl;
    private const string Workshop = "the-workshop";
    private static readonly string Reader = Persona.Id("reader");

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
    public async Task Signing_out_takes_the_mastheads_form_and_a_request_for_its_address_ends_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation)).Dispose();
        Assert.Contains("<details class=\"account menu participant is-you\">", await Html(browser.Client, "/"));

        using (var image = await browser.Client.GetAsync("/auth/logout", Cancellation))
            Assert.Equal(HttpStatusCode.NotFound, image.StatusCode);
        Assert.Contains("<details class=\"account menu participant is-you\">", await Html(browser.Client, "/"));

        using (var form = await browser.Client.PostAsync("/auth/logout", null, Cancellation))
            Assert.True(form.IsSuccessStatusCode || form.StatusCode == HttpStatusCode.Redirect);
        Assert.DoesNotContain("<details class=\"account menu participant is-you\">", await Html(browser.Client, "/"));
    }

    [Fact]
    public async Task The_page_as_html_and_json_carries_the_tangent_its_can_and_its_topics_by_last_activity_and_a_reply_moves_its_topic_to_the_top()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        var older = await Started("An older question", Start.AddMinutes(1));
        var newer = await Started("A newer question", Start.AddMinutes(2));
        var latest = await Started("Read this first", Start.AddMinutes(3));

        using (var json = await Json(tangentSpace.Client, "/t/" + Workshop))
        {
            var root = json.RootElement;
            Assert.Equal((Workshop, "The workshop", "Where things get made.", "topic", "public"),
                (root.GetProperty("slug").GetString(), root.GetProperty("name").GetString(), root.GetProperty("description").GetString(),
                    root.GetProperty("kind").GetString(), root.GetProperty("audience").GetString()));
            Assert.Equal($"{BaseUrl}/t/{Workshop}", root.GetProperty("address").GetString());
            Assert.Equal(["View"], Strings(root.GetProperty("can")));
            Assert.Equal([latest.Id, newer.Id, older.Id], Ids(root));
            var first = root.GetProperty("topics")[0];
            Assert.Equal(("read-this-first", "Read this first", "topic"), (first.GetProperty("slug").GetString(),
                first.GetProperty("title").GetString(), first.GetProperty("kind").GetString()));
            Assert.Equal($"{BaseUrl}/t/{Workshop}/read-this-first", first.GetProperty("address").GetString());
            Assert.Equal(("ada", "ada.example"), (first.GetProperty("author").GetProperty("id").GetString(), first.GetProperty("author").GetProperty("handle").GetString()));
            Assert.Equal("The words of the opening Post.", first.GetProperty("opening").GetString());
            Assert.False(root.TryGetProperty("next", out _));
        }

        Assert.IsType<Outcome<Post>.Done>(await new Replied(Tids.Mint(), older.Id, "A reply.", "ada", At: Start.AddMinutes(4)).Send(Cancellation));

        using (var json = await Json(tangentSpace.Client, "/t/" + Workshop))
        {
            Assert.Equal([older.Id, latest.Id, newer.Id], Ids(json.RootElement));
            Assert.Equal(1, json.RootElement.GetProperty("topics")[0].GetProperty("replyCount").GetInt32());
        }

        var html = await Html(tangentSpace.Client, "/t/" + Workshop);
        Assert.Contains("<h1 id=\"tangent-title\" class=\"tangent-title\">The workshop</h1>", html);
        Assert.Contains("<summary class=\"switcher-button\" aria-current=\"page\" aria-label=\"The workshop, switch Tangent\">", html);
        Assert.Contains($"href=\"{BaseUrl}/t/{Workshop}/an-older-question\"", html);
        var order = new[] { "An older question", "Read this first", "A newer question" }.Select(title => html.IndexOf(title, StringComparison.Ordinal)).ToList();
        Assert.All(order, index => Assert.True(index > 0));
        Assert.Equal(order.Order().ToList(), order);
        Assert.DoesNotContain("<dialog", html);
    }

    [Fact]
    public async Task More_topics_than_one_page_yield_a_cursor_and_the_next_page_continues_without_overlap_or_gap_and_no_answer_carries_a_count()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        var all = new List<Post>();
        // Three Topics share every instant, so the order rests on the Id where last activity is the same.
        for (var index = 0; index < 2 * TopicList.PageSize + 1; index++)
            all.Add(await Started("Topic " + index, Start.AddMinutes(index / 3)));

        var expected = all.OrderByDescending(topic => topic.LastActivityAt).ThenByDescending(topic => topic.Id, StringComparer.Ordinal)
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
    public async Task A_topic_started_between_two_pages_leads_the_list_and_the_second_page_continues_from_the_first_without_a_repeat_and_an_altered_or_foreign_cursor_is_refused_naming_after()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangent>.Done>(await new CreateTangent("garden", "The garden", "Where things grow.", PostKind.Topic, 2, Audience.Public, "leo").Send(Cancellation));
        var all = new List<Post>();
        for (var index = 0; index < TopicList.PageSize + 5; index++)
            all.Add(await Started("Topic " + index, Start.AddMinutes(index)));
        var expected = all.OrderByDescending(topic => topic.LastActivityAt).ThenByDescending(topic => topic.Id, StringComparer.Ordinal)
            .Select(topic => topic.Id).ToList();

        string next;
        List<string> first;
        using (var page = await Json(tangentSpace.Client, "/t/" + Workshop))
        {
            first = Ids(page.RootElement);
            next = new Uri(page.RootElement.GetProperty("next").GetString()!).PathAndQuery;
        }

        // Started ahead of the cursor, it moves no Topic of the next page onto the one already read.
        var between = await Started("Started between the pages", Start.AddDays(1));
        using (var page = await Json(tangentSpace.Client, next))
        {
            var second = Ids(page.RootElement);
            Assert.Empty(first.Intersect(second));
            Assert.DoesNotContain(between.Id, second);
            Assert.Equal(expected, first.Concat(second).ToList());
            Assert.False(page.RootElement.TryGetProperty("next", out _));
            Assert.True(page.RootElement.GetProperty("continued").GetBoolean());
        }

        using (var page = await Json(tangentSpace.Client, "/t/" + Workshop))
            Assert.Equal(between.Id, Ids(page.RootElement)[0]);

        var after = Uri.UnescapeDataString(next[(next.IndexOf("after=", StringComparison.Ordinal) + "after=".Length)..]);
        var altered = after[..^2] + (after[^2] == 'A' ? 'B' : 'A') + after[^1];
        foreach (var address in new[] { $"/t/{Workshop}?after={Uri.EscapeDataString(altered)}", $"/t/garden?after={Uri.EscapeDataString(after)}" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await tangentSpace.Client.SendAsync(request, Cancellation);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
            Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("after", out _));
        }
    }

    [Fact]
    public async Task A_signed_in_reader_sees_new_topics_marked_one_never_opened_with_no_count_a_mark_at_the_last_sequence_unmarks_one_a_later_reply_counts_it_and_a_visitor_sees_no_marks()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        var seen = await Started("Seen already", Start);
        var unseen = await Started("Never opened", Start.AddMinutes(1));
        Assert.IsType<Outcome<Post>.Done>(await new Replied(Tids.Mint(), seen.Id, "First reply.", "ada", At: Start.AddMinutes(2)).Send(Cancellation));
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation)).Dispose();

        using (var json = await Json(browser.Client, "/t/" + Workshop))
        {
            Assert.All(json.RootElement.GetProperty("topics").EnumerateArray(), topic => Assert.True(topic.GetProperty("new").GetBoolean()));
            Assert.Equal(["View", "Post", "Topic", "EditOwn", "RemoveOwn"], Strings(json.RootElement.GetProperty("can")));
        }

        var root = await Post.Get(seen.Id, Cancellation);
        Assert.Equal(1, root!.LastSequence);
        Assert.IsType<Outcome<Checkpoint>.Done>(await new Store<Checkpoint>(Checkpoint.At(Reader, Scope.Topic(seen.Id), root.LastSequence)).Send(Cancellation));

        using (var json = await Json(browser.Client, "/t/" + Workshop))
        {
            var marks = Marks(json.RootElement);
            Assert.Equal((false, (long?)0), marks[seen.Id]);
            Assert.Equal((true, (long?)null), marks[unseen.Id]);
        }

        Assert.IsType<Outcome<Post>.Done>(await new Replied(Tids.Mint(), seen.Id, "Second reply.", "ada", At: Start.AddMinutes(3)).Send(Cancellation));

        using (var json = await Json(browser.Client, "/t/" + Workshop))
            Assert.Equal((true, (long?)1), Marks(json.RootElement)[seen.Id]);
        var html = await Html(browser.Client, "/t/" + Workshop);
        Assert.Contains("<span class=\"new-dot\" role=\"img\" aria-label=\"Unread\"></span>", html);
        Assert.Contains("<span class=\"badge is-new\">1 new</span>", html);
        // The reader's account opens in the page, with the way to sign out and come back here.
        Assert.Contains("<details class=\"account menu participant is-you\">", html);
        Assert.Contains($"<form class=\"sign-out\" method=\"post\" action=\"{BaseUrl}/auth/logout?return={Uri.EscapeDataString("/t/" + Workshop)}\"><button type=\"submit\" class=\"account-item account-action\">", html);
        Assert.Contains("</svg>Sign out</button></form>", html);

        using (var json = await Json(tangentSpace.Client, "/t/" + Workshop))
            Assert.All(Marks(json.RootElement).Values, mark => Assert.Equal((false, (long?)0), mark));
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

        Assert.Equal((HttpStatusCode.NotFound, "absent"), (tangent.Status, tangent.Kind));
        Assert.Equal(tangent, topic);
        Assert.Equal(await Problem(tangentSpace.Client, "/t/nowhere"), tangent);
    }

    [Fact]
    public async Task A_visitor_on_a_private_place_receives_sign_in_and_a_reader_the_tangent_is_absent_to_receives_what_a_missing_tangent_receives()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangent>.Done>(await new CreateTangent("studio", "The studio", "", PostKind.Topic, 2, Audience.Private, "leo").Send(Cancellation));
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation)).Dispose();

        // A member of the place, where the Tangent's everyone overwrite denies View: the word 1.3 defines, absent,
        // in the answer an address without a Tangent gets.
        var absent = await Problem(browser.Client, "/t/studio");
        var missing = await Problem(browser.Client, "/t/nowhere");
        Assert.Equal((HttpStatusCode.NotFound, "application/problem+json", "absent"), (absent.Status, absent.MediaType, absent.Kind));
        Assert.Equal(missing, absent);
        // A visitor on the private Tangent of a public place: absent too, by the order access resolves in.
        Assert.Equal(missing, await Problem(tangentSpace.Client, "/t/studio"));

        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Audience = Audience.Private;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));

        var visitor = await Problem(tangentSpace.Client, "/t/" + Workshop);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn.Word), (visitor.Status, visitor.Word));
        Assert.Equal(visitor, await Problem(tangentSpace.Client, "/t/nowhere"));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/t/" + Workshop);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var page = await tangentSpace.Client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.Unauthorized, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync(Cancellation);
        Assert.Contains("Sign in to read here", html);
        Assert.DoesNotContain("The workshop", html);
    }

    [Fact]
    public async Task A_tangent_without_topics_renders_its_empty_state_and_the_start_affordance_shows_only_where_can_holds_topic()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        // An applicant: signed in, reading, and holding no verb but View.
        await Given.AParticipant(tangentSpace, Persona.Id("newcomer"), [], Standing.Applied, Cancellation);
        using var member = new Browser(tangentSpace);
        (await member.SignIn("reader", Reader, Cancellation)).Dispose();
        using var newcomer = new Browser(tangentSpace);
        (await newcomer.SignIn("newcomer", Persona.Id("newcomer"), Cancellation)).Dispose();
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
        await Place(tangentSpace);
        var topic = await Started("A question", Start);

        var first = await Tag(tangentSpace.Client, null);
        var second = await Tag(tangentSpace.Client, null);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(first.ETag, second.ETag);
        Assert.Equal(HttpStatusCode.NotModified, (await Tag(tangentSpace.Client, first.ETag)).Status);

        Assert.IsType<Outcome<Post>.Done>(await new Replied(Tids.Mint(), topic.Id, "An answer.", "ada", At: Start.AddMinutes(1)).Send(Cancellation));

        var after = await Tag(tangentSpace.Client, first.ETag);
        Assert.Equal(HttpStatusCode.OK, after.Status);
        Assert.NotEqual(first.ETag, after.ETag);
    }

    [Fact]
    public async Task A_row_carries_its_latest_reply_and_recent_authors_and_the_frame_lists_the_tangents_the_reader_may_view_and_who_reads()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangent>.Done>(await new CreateTangent("studio", "The studio", "", PostKind.Article, 2, Audience.Private, "leo").Send(Cancellation));
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        var topic = await Started("A question", Start);
        var reply = Tids.Mint();
        Assert.IsType<Outcome<Post>.Done>(await new Replied(Tids.Mint(), topic.Id, "First.", "leo", At: Start.AddMinutes(1)).Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await new Replied(reply, topic.Id, "The **latest** word.", "ada", At: Start.AddMinutes(2)).Send(Cancellation));

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
        Assert.Contains("<span class=\"tangent-row-detail\">Discussions · 1 topic</span>", WebUtility.HtmlDecode(visitorHtml));
        Assert.DoesNotContain("The studio", visitorHtml);
        Assert.Contains("<span class=\"topic-row-latest-line\"><b>ada.example</b> The latest word.</span>", visitorHtml);

        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation)).Dispose();
        var readerHtml = await Html(browser.Client, "/t/" + Workshop);
        Assert.DoesNotContain("masthead-sign-in", readerHtml);
        Assert.Contains(" · Member</span>", WebUtility.HtmlDecode(readerHtml));
        Assert.Contains($"<li><form class=\"sign-out\" method=\"post\" action=\"{BaseUrl}/auth/logout?return={Uri.EscapeDataString("/t/" + Workshop)}\">", readerHtml);
        Assert.Contains("data-theme-choice=\"dark\"", readerHtml[..readerHtml.IndexOf("account-items", StringComparison.Ordinal)]);
        Assert.DoesNotContain("The studio", readerHtml);
        Assert.Contains("<span class=\"badge is-new new-beside\">New</span>", readerHtml);
    }

    [Fact]
    public async Task A_topic_whose_root_is_removed_is_listed_saying_who_removed_it_as_html_and_json_and_a_removed_article_never_leads()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        var open = await Started("Still open", Start);
        var gone = await Started("Taken down", Start.AddMinutes(1));
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(gone.Id, "leo", Start.AddMinutes(2)).Send(Cancellation));

        using (var json = await Json(tangentSpace.Client, "/t/" + Workshop))
        {
            var topics = json.RootElement.GetProperty("topics").EnumerateArray().ToDictionary(topic => topic.GetProperty("id").GetString()!);
            var removed = topics[gone.Id].GetProperty("removed");
            Assert.Equal(("leo", Start.AddMinutes(2)), (removed.GetProperty("by").GetProperty("id").GetString(), removed.GetProperty("at").GetDateTimeOffset()));
            Assert.Equal(("Taken down", ""), (topics[gone.Id].GetProperty("title").GetString(), topics[gone.Id].GetProperty("opening").GetString()));
            Assert.False(topics[open.Id].TryGetProperty("removed", out _));
        }

        var html = await Html(tangentSpace.Client, "/t/" + Workshop);
        Assert.Contains("<a class=\"row topic is-removed\"", html);
        Assert.Matches("<span class=\"topic-row-removed\">Removed by leo\\.example <time ", html);
        Assert.Single(Regex.Matches(html, "is-removed"));

        // In a Tangent of articles the newest is removed: the next leads, and the removed one follows as a card.
        Assert.IsType<Outcome<Tangent>.Done>(await new CreateTangent("essays", "Essays", "", PostKind.Article, 2, Audience.Public, "leo").Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), "essays", "An older essay", "Words that stay.", "ada", At: Start.AddMinutes(3)).Send(Cancellation));
        var newer = Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), "essays", "A newer essay", "Words that go.", "ada", At: Start.AddMinutes(4)).Send(Cancellation)).Result;
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(newer.Id, "ada", Start.AddMinutes(5)).Send(Cancellation));

        var essays = await Html(tangentSpace.Client, "/t/essays");
        var lead = Regex.Match(essays, "<article class=\"article-card article-lead card topic\"[^>]*>.*?</article>", RegexOptions.Singleline);
        var card = Regex.Match(essays, "<article class=\"article-card card topic is-removed\"[^>]*>.*?</article>", RegexOptions.Singleline);
        Assert.True(lead.Success && card.Success, "the older essay leads and the removed one is a card");
        Assert.Contains("An older essay", lead.Value);
        Assert.Contains("A newer essay", card.Value);
        Assert.Matches("<p class=\"article-card-removed\">Removed by its author <time ", card.Value);
        Assert.DoesNotContain("min read", card.Value);
        Assert.True(lead.Index < card.Index);
    }

    [Fact]
    public async Task An_articles_tangent_narrows_by_month_and_by_author_across_all_its_articles_as_html_json_and_the_tool_its_rail_offering_each_with_how_many()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangent>.Done>(await new CreateTangent("essays", "Essays", "", PostKind.Article, 2, Audience.Public, "leo").Send(Cancellation));
        // Leo's two September essays lie past the first page, behind more October essays of Ada's than a page holds.
        var september = new List<string>();
        foreach (var day in new[] { 10, 12 })
            september.Add(Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), "essays", "September " + day, "Words of September.", "leo",
                At: new DateTimeOffset(2026, 9, day, 9, 0, 0, TimeSpan.Zero)).Send(Cancellation)).Result.Id);
        var october = new List<string>();
        for (var index = 0; index < TopicList.PageSize + 1; index++)
            october.Add(Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), "essays", "October " + index, "Words of October.", "ada",
                At: new DateTimeOffset(2026, 10, 1, 9, index, 0, TimeSpan.Zero)).Send(Cancellation)).Result.Id);

        using (var whole = await Json(tangentSpace.Client, "/t/essays"))
        {
            var root = whole.RootElement;
            Assert.DoesNotContain(Ids(root), september.Contains);
            Assert.Equal([("2026-10", "October 2026", 31, $"{BaseUrl}/t/essays?month=2026-10"), ("2026-09", "September 2026", 2, $"{BaseUrl}/t/essays?month=2026-09")],
                Choices(root.GetProperty("months")));
            Assert.Equal([("ada", "ada.example", 31, $"{BaseUrl}/t/essays?author=ada"), ("leo", "leo.example", 2, $"{BaseUrl}/t/essays?author=leo")],
                Choices(root.GetProperty("authors")));
            Assert.False(root.TryGetProperty("month", out _));
        }

        using (var narrowed = await Json(tangentSpace.Client, "/t/essays?month=2026-09"))
        {
            Assert.Equal(september.AsEnumerable().Reverse(), Ids(narrowed.RootElement));
            Assert.Equal("2026-09", narrowed.RootElement.GetProperty("month").GetString());
            Assert.False(narrowed.RootElement.TryGetProperty("next", out _));
        }

        using (var byLeo = await Json(tangentSpace.Client, "/t/essays?author=leo"))
        {
            Assert.Equal(september.AsEnumerable().Reverse(), Ids(byLeo.RootElement));
            Assert.Equal("leo", byLeo.RootElement.GetProperty("author").GetProperty("id").GetString());
        }

        using (var both = await Json(tangentSpace.Client, "/t/essays?month=2026-10&author=leo"))
            Assert.Empty(Ids(both.RootElement));

        // October holds more than a page: the next page stays narrowed and continues by the same cursor.
        var read = new List<string>();
        string? address = "/t/essays?month=2026-10";
        while (address is not null)
        {
            using var json = await Json(tangentSpace.Client, address);
            read.AddRange(Ids(json.RootElement));
            address = json.RootElement.TryGetProperty("next", out var next) ? new Uri(next.GetString()!).PathAndQuery : null;
            Assert.True(address is null || address.StartsWith("/t/essays?month=2026-10&after=", StringComparison.Ordinal), address);
        }

        Assert.Equal(october.AsEnumerable().Reverse(), read);

        var html = await Html(tangentSpace.Client, "/t/essays?month=2026-09");
        Assert.Contains("<a class=\"topic-view\" href=\"" + BaseUrl + "/t/essays?month=2026-09\" aria-current=\"page\">September 2026<span class=\"topic-view-count\">2</span></a>", html);
        Assert.Contains("<a class=\"topic-view\" href=\"" + BaseUrl + "/t/essays?author=ada\">ada.example<span class=\"topic-view-count\">31</span></a>", html);
        Assert.Contains("<a href=\"" + BaseUrl + "/t/essays\">All articles</a>", html);
        Assert.Contains("<h2 class=\"topic-list-title\">2 articles from September 2026</h2>", html);
        Assert.Contains("<h2 class=\"topic-list-title\">Articles by leo.example from September 2026</h2>", await Html(tangentSpace.Client, "/t/essays?month=2026-09&author=leo"));
        Assert.Contains("No articles by leo.example from October 2026.", await Html(tangentSpace.Client, "/t/essays?month=2026-10&author=leo"));

        using var refused = await tangentSpace.Client.GetAsync("/t/essays?month=2026-13", Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        using var tools = await ToolClient.Connect(tangentSpace);
        var called = await tools.Call("read_tangent", new { tangent = "essays", author = "leo" });
        Assert.False(called.Failed, called.Text);
        Assert.Equal(september.AsEnumerable().Reverse(), Ids(called.Structured!.Value));
    }

    [Fact]
    public async Task An_articles_tangent_lists_by_when_each_article_started_so_a_reply_moves_nothing_and_the_newest_leads_while_discussions_keep_latest_activity()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangent>.Done>(await new CreateTangent("essays", "Essays", "", PostKind.Article, 2, Audience.Public, "leo").Send(Cancellation));
        var older = Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), "essays", "The older essay", "Words of old.", "leo", At: Start).Send(Cancellation)).Result;
        var newer = Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), "essays", "The newer essay", "Words of new.", "leo", At: Start.AddMinutes(1)).Send(Cancellation)).Result;
        Assert.IsType<Outcome<Post>.Done>(await new Replied(Tids.Mint(), older.Id, "A late reply.", "ada", At: Start.AddMinutes(2)).Send(Cancellation));

        foreach (var path in new[] { "/t/essays", "/t/essays?author=leo", "/t/essays?month=2026-09" })
        {
            using var json = await Json(tangentSpace.Client, path);
            Assert.Equal([newer.Id, older.Id], Ids(json.RootElement));
        }

        var html = await Html(tangentSpace.Client, "/t/essays");
        Assert.Contains("<span class=\"topic-list-order\">newest first</span>", html);
        var lead = Regex.Match(html, "<article class=\"article-card article-lead card topic\"[^>]*>.*?</article>", RegexOptions.Singleline);
        Assert.True(lead.Success);
        Assert.Contains("The newer essay", lead.Value);

        var asked = await Started("Asked first", Start);
        var answered = await Started("Asked next", Start.AddMinutes(1));
        Assert.IsType<Outcome<Post>.Done>(await new Replied(Tids.Mint(), asked.Id, "An answer.", "leo", At: Start.AddMinutes(2)).Send(Cancellation));
        using (var discussions = await Json(tangentSpace.Client, "/t/" + Workshop))
            Assert.Equal([asked.Id, answered.Id], Ids(discussions.RootElement));
        Assert.Contains("<span class=\"topic-list-order\">latest activity first</span>", await Html(tangentSpace.Client, "/t/" + Workshop));
    }

    [Fact]
    public async Task More_articles_than_one_page_continue_by_when_each_started_each_on_exactly_one_page_however_their_replies_fall()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangent>.Done>(await new CreateTangent("essays", "Essays", "", PostKind.Article, 2, Audience.Public, "leo").Send(Cancellation));
        var started = new List<string>();
        for (var index = 0; index < TopicList.PageSize + 1; index++)
            started.Add(Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), "essays", "Essay " + index, "Words.", "leo",
                At: Start.AddMinutes(index)).Send(Cancellation)).Result.Id);
        // The oldest articles gain the newest replies, so by latest activity they would come first.
        for (var index = 0; index < 3; index++)
            Assert.IsType<Outcome<Post>.Done>(await new Replied(Tids.Mint(), started[index], "Late.", "ada", At: Start.AddHours(2 + index)).Send(Cancellation));

        var read = new List<string>();
        var pages = 0;
        string? address = "/t/essays";
        while (address is not null)
        {
            using var json = await Json(tangentSpace.Client, address);
            read.AddRange(Ids(json.RootElement));
            pages++;
            address = json.RootElement.TryGetProperty("next", out var next) ? new Uri(next.GetString()!).PathAndQuery : null;
        }

        Assert.Equal(2, pages);
        Assert.Equal(started.AsEnumerable().Reverse(), read);
    }

    [Fact]
    public async Task An_articles_tangents_rail_offers_the_authors_who_started_the_most_and_the_one_its_list_is_narrowed_to()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangent>.Done>(await new CreateTangent("essays", "Essays", "", PostKind.Article, 2, Audience.Public, "leo").Send(Cancellation));
        // More writers than the rail offers: the first ones each start two essays, the last two one each.
        var writers = Enumerable.Range(0, TopicsController.AuthorLimit + 2).Select(index => $"writer{index:D2}").ToList();
        foreach (var (writer, index) in writers.Select((writer, index) => (writer, index)))
        {
            await Given.AParticipant(tangentSpace, writer, [Roles.Member], ct: Cancellation);
            for (var essay = 0; essay < (index < TopicsController.AuthorLimit ? 2 : 1); essay++)
                Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), "essays", $"Essay {essay} of {writer}", "Words.", writer, At: Start.AddMinutes(index)).Send(Cancellation));
        }

        var most = writers.Take(TopicsController.AuthorLimit).Select(writer => (writer, writer + ".example", 2, $"{BaseUrl}/t/essays?author={writer}")).ToList();
        using (var whole = await Json(tangentSpace.Client, "/t/essays"))
            Assert.Equal(most, Choices(whole.RootElement.GetProperty("authors")));

        // Narrowed to a writer past them, the rail offers them too, current, and the list says how many they started.
        var last = writers[^1];
        using (var narrowed = await Json(tangentSpace.Client, "/t/essays?author=" + last))
        {
            Assert.Equal([.. most, (last, last + ".example", 1, $"{BaseUrl}/t/essays?author={last}")], Choices(narrowed.RootElement.GetProperty("authors")));
            Assert.Single(Ids(narrowed.RootElement));
        }

        var html = await Html(tangentSpace.Client, "/t/essays?author=" + last);
        Assert.Contains("<a class=\"topic-view\" href=\"" + BaseUrl + "/t/essays?author=" + last + "\" aria-current=\"page\">" + last + ".example<span class=\"topic-view-count\">1</span></a>", html);
        Assert.Contains("<h2 class=\"topic-list-title\">1 article by " + last + ".example</h2>", html);
        Assert.DoesNotContain("?author=" + writers[^2] + "\"", html);
    }

    [Fact]
    public async Task A_tangent_whose_months_and_authors_do_not_add_up_to_its_topics_is_counted_again_at_start()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        await Started("One", new DateTimeOffset(2026, 9, 30, 23, 59, 0, TimeSpan.Zero));
        await Started("Two", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), Workshop, "Three", "Words.", "leo", At: Start).Send(Cancellation));
        var counted = (await Tangent.Get(Workshop, Cancellation))!;
        Assert.True(counted.Tallied());
        var months = counted.Months.OrderBy(month => month.Key).ToList();
        var authors = counted.Authors.OrderBy(author => author.Key).ToList();
        Assert.Equal(new KeyValuePair<string, int>[] { new("2026-09", 2), new("2026-10", 1) }, months);
        Assert.Equal(new KeyValuePair<string, int>[] { new("ada", 2), new("leo", 1) }, authors);

        (counted.Months, counted.Authors) = ([], []);
        await counted.Save(Cancellation);
        await TallyTopics.AtStart(NullLogger.Instance, Cancellation);

        var tallied = (await Tangent.Get(Workshop, Cancellation))!;
        Assert.Equal(months, tallied.Months.OrderBy(month => month.Key));
        Assert.Equal(authors, tallied.Authors.OrderBy(author => author.Key));
    }

    [Fact]
    public void The_version_differs_across_midnight_utc_for_the_same_page_since_the_day_changes_how_its_times_read()
    {
        var at = new DateTimeOffset(2026, 10, 29, 15, 0, 0, TimeSpan.Zero);
        var author = new global::Tangent.Render.Person("ada", "ada.example", "Ada", "person", null);
        var topic = new global::Tangent.Render.TopicEntry(Tids.Mint(), "a-question", "A question", "topic", $"{BaseUrl}/t/{Workshop}/a-question", author,
            "Words.", 1, 0, at, at, null, [author], false, 0);
        var today = new TangentPage(Workshop, "The workshop", "", "topic", "public", $"{BaseUrl}/t/{Workshop}", ["View"], [topic], null,
            false, null, null, [], "Tangent Space", BaseUrl + "/", null, new DateOnly(2026, 10, 29));
        var tomorrow = today with { Day = new DateOnly(2026, 10, 30) };

        Assert.Equal(TangentPage.Version(today), TangentPage.Version(today with { }));
        Assert.NotEqual(TangentPage.Version(today), TangentPage.Version(tomorrow));
        Assert.Equal("3:00 PM", Render.Wording.When(at, today.Day));
        Assert.Equal("Oct 29", Render.Wording.When(at, tomorrow.Day));
        Assert.Equal("Oct 29, 2026", Render.Wording.When(at, new DateOnly(2027, 1, 1)));
    }

    /// <summary>The place every proof starts from: established, with an administrator who sets up the public Tangent,
    /// and a member who starts its Topics.</summary>
    private static async Task Place(TangentSpaceFixture tangentSpace)
    {
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ada", [Roles.Member], ct: Cancellation);
        Assert.IsType<Outcome<Tangent>.Done>(await new CreateTangent(Workshop, "The workshop", "Where things get made.", PostKind.Topic, 1, Audience.Public, "leo").Send(Cancellation));
    }

    private static async Task<Post> Started(string title, DateTimeOffset at)
        => Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), Workshop, title, "The *words* of the [opening](https://example.com) Post.", "ada", At: at).Send(Cancellation)).Result;

    private static List<string> Ids(JsonElement page)
        => page.GetProperty("topics").EnumerateArray().Select(topic => topic.GetProperty("id").GetString()!).ToList();

    private static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToList();

    /// <summary>Each way a list narrows, as its JSON says it: its key, name, count and address.</summary>
    private static List<(string, string, int, string)> Choices(JsonElement array) => array.EnumerateArray()
        .Select(choice => (choice.GetProperty("key").GetString()!, choice.GetProperty("name").GetString()!, choice.GetProperty("count").GetInt32(),
            choice.GetProperty("address").GetString()!))
        .ToList();

    /// <summary>What the page says is new in each Topic: whether it is, and how many of its Posts, null where it is new
    /// with no count.</summary>
    private static Dictionary<string, (bool New, long? NewPosts)> Marks(JsonElement page)
        => page.GetProperty("topics").EnumerateArray().ToDictionary(topic => topic.GetProperty("id").GetString()!,
            topic => (topic.GetProperty("new").GetBoolean(),
                topic.TryGetProperty("newPosts", out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt64() : (long?)null));

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

    private sealed record Answer(HttpStatusCode Status, string? MediaType, string? Title, string? Detail, string? Word, string? Kind);

    private static async Task<Answer> Problem(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, Cancellation);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        string? Text(string name) => json.RootElement.TryGetProperty(name, out var value) ? value.GetString() : null;
        return new Answer(response.StatusCode, response.Content.Headers.ContentType?.MediaType, Text("title"), Text("detail"), Text("word"), Text("kind"));
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
