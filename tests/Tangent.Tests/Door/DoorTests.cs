using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Tangents;
using Xunit;

namespace Tangent.Space;

/// <summary>The door both ways: a reply's target; what is new to a signed-in reader since their checkpoints, grouped,
/// with what they may not view and what they have seen left out; a window that could not hold everything saying so;
/// marking the door seen; and the front page a visitor reads, or the gate on a private place.</summary>
public sealed class DoorTests(ITestOutputHelper output)
{
    private const string BaseUrl = TangentSpaceFixture.DefaultBaseUrl;
    private const string Workshop = "the-workshop";
    private const string Articles = "articles";
    private static readonly string Reader = Persona.Id("reader");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A Tangent Space where Koan's test connector signs people in, and no profile job reaches the network.</summary>
    private static readonly Dictionary<string, string?> TestConnector = new()
    {
        ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
        ["Tangent:Profile:BaseAddress"] = "http://127.0.0.1:1/",
        ["Tangent:Profile:Timeout"] = "00:00:01"
    };

    [Fact]
    public async Task A_reply_to_another_participants_post_targets_its_author_and_a_reply_to_ones_own_post_targets_no_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        var topic = await Started(Workshop, "A question", "ada");

        var toAda = await Replied(topic, "leo");
        var adaToHerself = await Replied(topic, "ada");
        var toLeo = await Replied(topic, "ada", parent: toAda.Id);
        var leoToHimself = await Replied(topic, "leo", parent: toAda.Id);

        var targets = (await Event.All(Cancellation)).Where(e => e.Kind == EventKind.PostCreated)
            .ToDictionary(e => e.PostId!, e => e.TargetId);
        Assert.Equal(("ada", (string?)null, "leo", (string?)null),
            (targets[toAda.Id], targets[adaToHerself.Id], targets[toLeo.Id], targets[leoToHimself.Id]));
        Assert.Null(targets[topic.Id]);
    }

    [Fact]
    public async Task Signed_in_with_a_checkpoint_behind_the_door_groups_replies_mentions_new_per_tangent_and_new_articles_without_the_readers_own_and_names_what_it_read_through()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        var mine = await Started(Workshop, "My question", Reader);
        var before = await Replied(mine, "ada");
        var since = await Mark(Reader);

        var reply = await Replied(mine, "ada");
        var other = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Workshop, "Ada asks", $"A question for [@reader](/p/{Reader}).", "ada").Send(Cancellation)).Result;
        var article = await Started(Articles, "On lanterns", "leo");
        // The reader's own reply is never news to them, and it leaves the reply before it, which they have not read, new.
        var own = await Replied(mine, Reader, body: "My own words in reply.");
        var through = await Event.Log.Highest(Cancellation);

        using var browser = await SignedIn(tangentSpace);
        using (var json = await Json(browser.Client, "/"))
        {
            var news = json.RootElement.GetProperty("news");
            Assert.Equal((since, through), (news.GetProperty("from").GetInt64(), news.GetProperty("through").GetInt64()));
            Assert.False(news.GetProperty("empty").GetBoolean());
            Assert.Equal((Reader, "reader.example"), (json.RootElement.GetProperty("who").GetProperty("id").GetString(),
                json.RootElement.GetProperty("who").GetProperty("handle").GetString()));

            var replies = news.GetProperty("replies");
            Assert.Equal((1, false), (replies.GetProperty("count").GetInt32(), replies.GetProperty("atLeast").GetBoolean()));
            var item = replies.GetProperty("posts")[0];
            Assert.Equal((reply.Id, "ada", "My question", $"{BaseUrl}/t/{Workshop}/my-question/{reply.Id}"),
                (item.GetProperty("postId").GetString(), item.GetProperty("who").GetProperty("id").GetString(),
                    item.GetProperty("topic").GetString(), item.GetProperty("address").GetString()));

            Assert.Equal([other.Id], PostIds(news.GetProperty("mentions")));
            Assert.Equal([article.Id], PostIds(news.GetProperty("articles")));
            var perTangent = news.GetProperty("perTangent").EnumerateArray().ToDictionary(tangent => tangent.GetProperty("slug").GetString()!,
                tangent => (tangent.GetProperty("topics").GetInt32(), tangent.GetProperty("replies").GetInt32(), tangent.GetProperty("atLeast").GetBoolean()));
            Assert.Equal((1, 1, false), perTangent[Workshop]);
            Assert.Equal((1, 0, false), perTangent[Articles]);

            var text = json.RootElement.GetRawText();
            Assert.DoesNotContain(own.Id, text);
            Assert.DoesNotContain(before.Id, text);
            var form = news.GetProperty("seen");
            Assert.Equal(($"{BaseUrl}/seen", through.ToString(CultureInfo.InvariantCulture)), (form.GetProperty("action").GetString(), Writing.Valued(form, SeenController.ThroughField)));
        }

        var html = await Html(browser.Client, "/");
        Assert.Contains("Since you were here: 1 reply to you, 1 mention of you, 1 new article.",
            WebUtility.HtmlDecode(html));
        Assert.Contains($"name=\"{SeenController.ThroughField}\" value=\"{through.ToString(CultureInfo.InvariantCulture)}\"", html);
        Assert.Contains("Mark all seen", html);
        Assert.Contains($"href=\"{BaseUrl}/t/{Workshop}/my-question/{reply.Id}\"", html);
        Assert.DoesNotContain("My own words", html);
        Assert.DoesNotContain("<dialog", html);
    }

    [Fact]
    public async Task Past_five_of_a_kind_the_door_carries_every_one_draws_five_and_opens_the_rest_in_place_and_the_account_menu_opens_the_readers_own_profile()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        var mine = await Started(Workshop, "My question", Reader);
        await Mark(Reader);
        var replies = new List<Post>();
        for (var index = 1; index <= 7; index++) replies.Add(await Replied(mine, "ada", body: "Answer number " + index + "."));
        var newest = Enumerable.Reverse(replies).ToList();

        using var browser = await SignedIn(tangentSpace);
        using (var json = await Json(browser.Client, "/"))
        {
            var group = json.RootElement.GetProperty("news").GetProperty("replies");
            Assert.Equal((7, false), (group.GetProperty("count").GetInt32(), group.GetProperty("atLeast").GetBoolean()));
            Assert.Equal(newest.Select(reply => reply.Id), PostIds(group));
            var who = json.RootElement.GetProperty("who");
            Assert.Equal(("person", $"{BaseUrl}/p/reader.example"), (who.GetProperty("kind").GetString(), who.GetProperty("profile").GetString()));
        }

        // The newest five are rows; the two oldest wait behind one line in the same list, which opens them in place.
        var html = await Html(browser.Client, "/");
        var line = html.IndexOf("<details class=\"door-news-more\"><summary>and 2 more</summary>", StringComparison.Ordinal);
        Assert.True(line > 0, "The group has no line of the rest.");
        Assert.All(newest.Take(Door.Group.Drawn), reply => Assert.InRange(html.IndexOf("/my-question/" + reply.Id, StringComparison.Ordinal), 0, line));
        Assert.All(newest.Skip(Door.Group.Drawn), reply => Assert.True(html.IndexOf("/my-question/" + reply.Id, StringComparison.Ordinal) > line));
        Assert.Contains($"<a class=\"account-item\" href=\"{BaseUrl}/p/reader.example\">", html);
        Assert.True(html.IndexOf("Your profile", StringComparison.Ordinal) < html.IndexOf("Bring your agent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_places_manager_makes_it_theirs_in_first_steps_each_press_returning_to_the_door_until_they_close_them()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("leo", "leo", Cancellation)).Dispose();

        using (var json = await Json(browser.Client, "/"))
        {
            var steps = json.RootElement.GetProperty("firstSteps");
            Assert.Equal(BaseUrl + "/t/" + Articles + "/new", steps.GetProperty("firstTopic").GetString());
            Assert.Equal(BaseUrl + "/bring-your-agent", steps.GetProperty("bringYourAgent").GetString());
            Assert.Equal(BaseUrl + FirstStepsController.ClosePath, steps.GetProperty("close").GetProperty("action").GetString());
            var purposes = steps.GetProperty("purposes").EnumerateArray().ToDictionary(purpose => purpose.GetProperty("name").GetString()!, purpose => purpose);
            Assert.Equal(BaseUrl + "/t/" + Workshop, purposes["The workshop"].GetProperty("address").GetString());
            Assert.Equal(BaseUrl + "/t/new", purposes["Off the record"].GetProperty("add").GetProperty("action").GetString());
        }
        Assert.Contains("Make it yours.", WebUtility.HtmlDecode(await Html(browser.Client, "/")));

        // A press on the door sends the settings' own write and comes back to the door; another host is never where it goes.
        using (var jade = await browser.Client.PostAsync(TangentSpaceSettingsController.AppearancePath,
                   Form(new() { [TangentSpaceSettingsController.PaletteField] = "jade", [PostWrites.ReturnField] = "/" }), Cancellation))
            Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/"), (jade.StatusCode, jade.Headers.Location?.OriginalString));
        Assert.Equal(Palette.Jade, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Appearance.Palette);
        using (var away = await browser.Client.PostAsync(TangentSpaceSettingsController.AppearancePath,
                   Form(new() { [TangentSpaceSettingsController.PaletteField] = "iris", [PostWrites.ReturnField] = "//elsewhere.example/" }), Cancellation))
            Assert.Equal(BaseUrl + TangentSpaceSettingsController.AppearancePath, away.Headers.Location?.OriginalString);
        using (var created = await browser.Client.PostAsync("/t/new", Form(new()
               {
                   ["name"] = "Off the record", ["description"] = "For whoever is let in.", ["kind"] = "topic", ["audience"] = "private", [PostWrites.ReturnField] = "/"
               }), Cancellation))
            Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/"), (created.StatusCode, created.Headers.Location?.OriginalString));

        using (var closed = await browser.Client.PostAsync(FirstStepsController.ClosePath, Form(new()), Cancellation))
            Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/"), (closed.StatusCode, closed.Headers.Location?.OriginalString));
        using var after = await Json(browser.Client, "/");
        Assert.False(after.RootElement.TryGetProperty("firstSteps", out var left) && left.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task A_waiting_applicants_news_begins_at_their_arrival_so_what_the_place_held_is_not_new_to_them()
    {
        var applicant = Persona.Id("applicant4door");
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Admission = Admission.Apply;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));
        var topic = await Started(Workshop, "What should we build?", "ada");
        await Replied(topic, "leo");

        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("applicant", applicant, Cancellation, handle: "applicant.example", comeBack: "/t")).Dispose();
        var arrival = Assert.Single(await Event.Query(e => e.Kind == EventKind.StandingChanged && e.TargetId == applicant, Cancellation));
        Assert.Equal(arrival.Seq, (await Participant.Get(applicant, Cancellation))?.ArrivedSeq);
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(applicant, Scope.Place), Cancellation));

        using var json = await Json(browser.Client, "/");
        var news = json.RootElement.GetProperty("news");
        Assert.True(news.GetProperty("empty").GetBoolean());
        Assert.Empty(news.GetProperty("perTangent").EnumerateArray());
        Assert.Equal(Capabilities.Apply.Word, json.RootElement.GetProperty("gate").GetString());
        Assert.Contains("Nothing new since you were here.", WebUtility.HtmlDecode(await Html(browser.Client, "/")));
    }

    [Fact]
    public async Task A_newcomers_news_begins_at_their_arrival_so_what_the_place_held_is_not_new_and_what_follows_is()
    {
        var newcomer = Persona.Id("newcomer");
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        var topic = await Started(Workshop, "What should we build?", "ada");
        await Replied(topic, "leo");
        await Started(Articles, "On lanterns", "leo");

        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("newcomer", newcomer, Cancellation, handle: "newcomer.example", comeBack: "/t")).Dispose();
        var arrival = Assert.Single(await Event.Query(e => e.Kind == EventKind.StandingChanged && e.TargetId == newcomer, Cancellation));
        Assert.Equal(arrival.Seq, (await Participant.Get(newcomer, Cancellation))?.ArrivedSeq);
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(newcomer, Scope.Place), Cancellation));

        var first = WebUtility.HtmlDecode(await Html(browser.Client, "/"));
        Assert.Contains("Nothing new since you were here.", first);
        using (var json = await Json(browser.Client, "/"))
            Assert.Equal(arrival.Seq, json.RootElement.GetProperty("news").GetProperty("from").GetInt64());
        Assert.Contains("What should we build?", first);
        Assert.DoesNotContain("Mark all seen", first);
        Assert.DoesNotContain("class=\"new-dot\"", first);

        await Replied(topic, "ada");
        var next = WebUtility.HtmlDecode(await Html(browser.Client, "/"));
        Assert.Contains("Since you were here: 1 new post in The workshop.", next);
        Assert.Contains("Mark all seen", next);
    }

    [Fact]
    public async Task A_get_or_head_of_the_door_and_any_page_writes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        // The reader has a reply to them they have not seen.
        var mine = await Started(Workshop, "My question", Reader);
        await Replied(mine, "ada");
        using var browser = await SignedIn(tangentSpace);
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(Reader, Scope.Place), Cancellation));
        var before = await Written();

        foreach (var path in new[] { "/", "/t", "/t/" + Workshop, "/t/" + Workshop + "/" + mine.Slug, "/events?since=0", "/search?q=question" })
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            using var response = await browser.Client.SendAsync(request, Cancellation);
            output.WriteLine($"{method} {path}: {(int)response.StatusCode}");
            Assert.True(method == HttpMethod.Head || response.IsSuccessStatusCode, $"{method} {path} answered {response.StatusCode}");
        }

        Assert.Equal(before, await Written());
    }

    /// <summary>What a read could write: the log, every checkpoint and every Offer decided.</summary>
    private static async Task<string> Written()
    {
        var checkpoints = (await Checkpoint.All(Cancellation)).OrderBy(checkpoint => checkpoint.Id, StringComparer.Ordinal)
            .Select(checkpoint => $"{checkpoint.Id}={checkpoint.Seq}");
        return $"{await Event.Log.Highest(Cancellation)}/{await Event.Count.Exact(Cancellation)}/{await Offers.Offer.Count.Exact(Cancellation)}: "
               + string.Join(", ", checkpoints);
    }

    [Fact]
    public async Task An_event_in_a_tangent_the_reader_may_not_view_is_in_no_group_and_a_topic_checkpoint_past_an_event_hides_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new CreateTangent("studio", "The studio", "", PostKind.Topic, 3, Audience.Private, "leo").Send(Cancellation));
        var mine = await Started(Workshop, "My question", Reader);
        await Mark(Reader);

        var first = await Replied(mine, "ada");
        var second = await Replied(mine, "ada");
        var secret = await Started("studio", "A private matter", "leo");
        Assert.IsType<Outcome<Event>.Done>(await new Mentioned("studio", secret.Id, secret.Id, "leo", Reader).Send(Cancellation));
        Assert.IsType<Outcome<Checkpoint>.Done>(await new Store<Checkpoint>(Checkpoint.At(Reader, Scope.Topic(mine.Id), first.Sequence)).Send(Cancellation));

        using var browser = await SignedIn(tangentSpace);
        using var json = await Json(browser.Client, "/");
        var news = json.RootElement.GetProperty("news");
        Assert.Equal([second.Id], PostIds(news.GetProperty("replies")));
        Assert.Equal(0, news.GetProperty("mentions").GetProperty("count").GetInt32());
        Assert.Equal([(Workshop, 0, 1)], news.GetProperty("perTangent").EnumerateArray()
            .Select(tangent => (tangent.GetProperty("slug").GetString()!, tangent.GetProperty("topics").GetInt32(), tangent.GetProperty("replies").GetInt32())).ToList());
        var text = json.RootElement.GetRawText();
        Assert.DoesNotContain("studio", text);
        Assert.DoesNotContain("A private matter", text);
        Assert.DoesNotContain(secret.Id, text);
        Assert.DoesNotContain(first.Id, text);

        var html = await Html(browser.Client, "/");
        Assert.DoesNotContain("The studio", html);
        Assert.DoesNotContain("A private matter", html);
    }

    [Fact]
    public async Task More_events_than_the_window_yield_at_least_counts_and_no_total_and_the_window_reads_are_index_ranges_with_a_limit()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        var topic = await Started(Workshop, "A long conversation", "ada");
        for (var index = 0; index < LogWindow.Size + 5; index++) await Replied(topic, index % 2 == 0 ? "ada" : "leo");

        using var browser = await SignedIn(tangentSpace);
        using (var json = await Json(browser.Client, "/"))
        {
            var news = json.RootElement.GetProperty("news");
            var workshop = Assert.Single(news.GetProperty("perTangent").EnumerateArray());
            Assert.True(workshop.GetProperty("atLeast").GetBoolean());
            Assert.InRange(workshop.GetProperty("topics").GetInt32() + workshop.GetProperty("replies").GetInt32(), 1, LogWindow.Size);
            Assert.True(news.GetProperty("articles").GetProperty("atLeast").GetBoolean());
            Assert.True(json.RootElement.GetProperty("around").GetProperty("atLeast").GetBoolean());
            Assert.DoesNotContain(Names(json.RootElement), name => name.Contains("total", StringComparison.OrdinalIgnoreCase));
        }

        var html = WebUtility.HtmlDecode(await Html(browser.Client, "/"));
        Assert.Contains("Since you were here: at least ", html);
        Assert.Contains("new posts in The workshop", html);

        // The two reads LogWindow makes, as Koan's SQLite connector composes them, answer from their indexes as ranges
        // with a limit and no sort of their own.
        var plans = Plans(tangentSpace.DatabasePath);
        Assert.All(plans, plan => Assert.Contains(" LIMIT ", plan.Sql, StringComparison.Ordinal));
        Assert.Contains(plans[0].Plan, detail => detail.Contains("USING INDEX ix_event_seq", StringComparison.Ordinal));
        Assert.Contains(plans[1].Plan, detail => detail.StartsWith("SEARCH", StringComparison.Ordinal)
            && detail.Contains("USING INDEX ix_event_targets", StringComparison.Ordinal) && detail.Contains("=? AND", StringComparison.Ordinal));
        Assert.All(plans, plan => Assert.DoesNotContain(plan.Plan, detail => detail.Contains("TEMP B-TREE", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Marking_the_door_seen_moves_the_place_checkpoint_to_what_it_read_through_never_backwards_and_the_door_is_quiet_with_a_new_etag_until_something_happens()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        var mine = await Started(Workshop, "My question", Reader);
        await Replied(mine, "ada");
        using var browser = await SignedIn(tangentSpace);

        using (var json = await Json(browser.Client, "/"))
            Assert.True(json.RootElement.GetProperty("alive")[0].GetProperty("topic").GetProperty("new").GetBoolean());
        // The door with the form is never stored, and answers unchanged while nothing changes.
        var (status, busy, through, _, caching) = await Read(browser.Client, null);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("no-store", caching);
        Assert.Equal(HttpStatusCode.NotModified, (await Read(browser.Client, busy)).Status);
        Assert.Equal(await Event.Log.Highest(Cancellation), through);
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(Reader, Scope.Place), Cancellation));

        using (var marked = await MarkSeen(browser, through))
        {
            Assert.Equal(HttpStatusCode.SeeOther, marked.StatusCode);
            Assert.Equal("/", marked.Headers.Location?.OriginalString);
        }
        Assert.Equal(through, (await Checkpoint.Get(Checkpoint.Key(Reader, Scope.Place), Cancellation))!.Seq);

        var (_, quiet, _, _, quietCaching) = await Read(browser.Client, null);
        Assert.NotEqual(busy, quiet);
        Assert.Equal("private", quietCaching);
        Assert.Equal(HttpStatusCode.NotModified, (await Read(browser.Client, quiet)).Status);
        using (var json = await Json(browser.Client, "/"))
        {
            var news = json.RootElement.GetProperty("news");
            Assert.True(news.GetProperty("empty").GetBoolean());
            Assert.False(news.TryGetProperty("seen", out var seen) && seen.ValueKind != JsonValueKind.Null);
            Assert.False(json.RootElement.GetProperty("alive")[0].GetProperty("topic").GetProperty("new").GetBoolean());
        }
        Assert.Contains("Nothing new since you were here.", await Html(browser.Client, "/"));

        using (var backwards = await MarkSeen(browser, through - 1))
            Assert.Equal(HttpStatusCode.SeeOther, backwards.StatusCode);
        Assert.Equal(through, (await Checkpoint.Get(Checkpoint.Key(Reader, Scope.Place), Cancellation))!.Seq);

        var later = await Replied(mine, "ada");
        using (var json = await Json(browser.Client, "/"))
            Assert.Equal([later.Id], PostIds(json.RootElement.GetProperty("news").GetProperty("replies")));
    }

    [Fact]
    public async Task A_visitor_reads_a_shelf_of_each_tangents_latest_past_what_the_front_page_shows()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        var topics = new List<Post>();
        for (var index = 0; index < DoorController.AliveLimit + 2; index++) topics.Add(await Started(Workshop, $"Question {index}", "ada"));
        await Started(Articles, "On lanterns", "leo");

        using (var json = await Json(tangentSpace.Client, "/"))
        {
            // The one article is featured above, so its Tangent has no shelf rather than an empty one.
            var shelves = json.RootElement.GetProperty("shelves");
            Assert.Equal([Workshop], shelves.EnumerateArray().Select(shelf => shelf.GetProperty("slug").GetString()!).ToList());
            var workshop = shelves[0];
            Assert.Equal((DoorController.AliveLimit + 2, $"{BaseUrl}/t/{Workshop}"), (workshop.GetProperty("topicCount").GetInt32(), workshop.GetProperty("address").GetString()));
            Assert.Equal([topics[1].Id, topics[0].Id], workshop.GetProperty("topics").EnumerateArray().Select(topic => topic.GetProperty("id").GetString()!).ToList());
        }

        var html = WebUtility.HtmlDecode(await Html(tangentSpace.Client, "/"));
        Assert.Contains($"<a href=\"{BaseUrl}/t/{Workshop}\">The workshop</a></h2>", html);
        Assert.Contains("All 8 topics", html);
    }

    [Fact]
    public async Task A_visitor_on_a_public_place_reads_the_front_page_and_on_a_private_one_the_sign_in_gate()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        var topic = await Started(Workshop, "What should we build?", "ada");
        await Replied(topic, "leo");
        var article = await Started(Articles, "On lanterns", "leo");

        using (var json = await Json(tangentSpace.Client, "/"))
        {
            var root = json.RootElement;
            Assert.Equal(("Lantern Works", $"{BaseUrl}/", "leo"), (root.GetProperty("name").GetString(), root.GetProperty("address").GetString(),
                root.GetProperty("proprietor").GetProperty("id").GetString()));
            Assert.Equal(["View"], root.GetProperty("can").EnumerateArray().Select(item => item.GetString()!).ToList());
            Assert.False(root.TryGetProperty("news", out _));
            Assert.False(root.TryGetProperty("who", out _));
            Assert.Equal($"{BaseUrl}/sign-in?return=%2F", root.GetProperty("signIn").GetString());
            Assert.Equal([Articles, Workshop], root.GetProperty("tangents").EnumerateArray().Select(tangent => tangent.GetProperty("slug").GetString()!).ToList());
            Assert.Equal([article.Id], Shown(root.GetProperty("featured")));
            Assert.Equal([topic.Id], Shown(root.GetProperty("alive")));
            Assert.Equal($"{BaseUrl}/t/{Workshop}/what-should-we-build", root.GetProperty("alive")[0].GetProperty("topic").GetProperty("address").GetString());
            var around = root.GetProperty("around");
            Assert.Equal((2, false), (around.GetProperty("count").GetInt32(), around.GetProperty("atLeast").GetBoolean()));
            Assert.Equal(["leo", "ada"], around.GetProperty("people").EnumerateArray().Select(person => person.GetProperty("id").GetString()!).ToList());
            Assert.Equal(1, root.GetProperty("api").GetInt32());
        }

        var html = WebUtility.HtmlDecode(await Html(tangentSpace.Client, "/"));
        Assert.Contains("<span class=\"masthead-name\">Lantern Works</span>", html);
        Assert.Matches("The place of <a class=\"participant-name\" href=\"[^\"]*/p/leo.example\">leo.example</a>, @leo.example", html);
        Assert.Contains("What should we build?", html);
        Assert.Contains("On lanterns", html);
        Assert.Contains($"<a class=\"masthead-sign-in\" href=\"{BaseUrl}/sign-in?return=%2F\">Sign in</a>", html);
        Assert.DoesNotContain("Since you were here", html);

        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Audience = Audience.Private;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));

        using (var request = new HttpRequestMessage(HttpMethod.Get, "/"))
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var gated = await tangentSpace.Client.SendAsync(request, Cancellation);
            using var problem = JsonDocument.Parse(await gated.Content.ReadAsStringAsync(Cancellation));
            Assert.Equal((HttpStatusCode.Unauthorized, "application/problem+json", Capabilities.SignIn.Word),
                (gated.StatusCode, gated.Content.Headers.ContentType?.MediaType, problem.RootElement.GetProperty("word").GetString()));
        }

        using (var request = new HttpRequestMessage(HttpMethod.Get, "/"))
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            using var page = await tangentSpace.Client.SendAsync(request, Cancellation);
            var gatedHtml = await page.Content.ReadAsStringAsync(Cancellation);
            Assert.Equal(HttpStatusCode.Unauthorized, page.StatusCode);
            Assert.Contains("Sign in to read here", gatedHtml);
            Assert.DoesNotContain("What should we build?", gatedHtml);
        }
    }

    [Fact]
    public async Task An_event_in_a_tangent_the_reader_may_not_view_moves_neither_what_the_door_read_through_nor_its_body_nor_its_etag()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new CreateTangent("studio", "The studio", "", PostKind.Topic, 3, Audience.Private, "leo").Send(Cancellation));
        var since = await Mark(Reader);
        using var browser = await SignedIn(tangentSpace);

        var before = await Read(browser.Client, null);
        Assert.Equal(since, before.Through);
        await Started("studio", "A private matter", "leo");
        Assert.True(await Event.Log.Highest(Cancellation) > since);

        var after = await Read(browser.Client, null);
        Assert.Equal((since, before.Body, before.ETag), (after.Through, after.Body, after.ETag));
        Assert.Equal(HttpStatusCode.NotModified, (await Read(browser.Client, before.ETag)).Status);
    }

    [Fact]
    public async Task At_exactly_the_tangent_limit_the_door_lists_every_tangent_and_claims_no_more_and_one_more_it_may_view_makes_it_at_least()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        for (var index = 2; index < DoorController.TangentLimit; index++) await Given.ATangent(tangentSpace, "tangent-" + index, ct: Cancellation);
        // A Tangent the reader may not view, last by order: the one row past the limit, which claims nothing.
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new CreateTangent("hidden", "Hidden", "", PostKind.Topic, 99, Audience.Private, "leo").Send(Cancellation));

        using (var json = await Json(tangentSpace.Client, "/"))
        {
            Assert.Equal(DoorController.TangentLimit, json.RootElement.GetProperty("tangents").GetArrayLength());
            Assert.False(json.RootElement.GetProperty("moreTangents").GetBoolean());
        }
        var html = await Html(tangentSpace.Client, "/");
        Assert.Contains($"<span><b>{DoorController.TangentLimit}</b> Tangents</span>", html);
        Assert.DoesNotContain("at least", html);

        await Given.ATangent(tangentSpace, "one-more", ct: Cancellation);
        using (var json = await Json(tangentSpace.Client, "/"))
        {
            Assert.Equal(DoorController.TangentLimit, json.RootElement.GetProperty("tangents").GetArrayLength());
            Assert.True(json.RootElement.GetProperty("moreTangents").GetBoolean());
        }
        Assert.Contains($"at least <b>{DoorController.TangentLimit}</b> Tangents", await Html(tangentSpace.Client, "/"));
    }

    /// <summary>The place every proof starts from: established as Lantern Works, Leo's place, with Leo an
    /// administrator, Ada and the reader members, a public discussions Tangent and a public articles Tangent.</summary>
    private static async Task Place(TangentSpaceFixture tangentSpace)
    {
        var space = await Given.AnEstablishedTangentSpace(tangentSpace, "Lantern Works", ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ada", [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        space.ProprietorId = "leo";
        space.OperatorId = "leo";
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new CreateTangent(Workshop, "The workshop", "Where things get made.", PostKind.Topic, 2, Audience.Public, "leo").Send(Cancellation));
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new CreateTangent(Articles, "Articles", "", PostKind.Article, 1, Audience.Public, "leo").Send(Cancellation));
    }

    private static async Task<Post> Started(string tangent, string title, string author)
        => Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), tangent, title, "The opening words of " + title + ".", author).Send(Cancellation)).Result;

    private static async Task<Post> Replied(Post topic, string author, string? parent = null, string body = "A reply.")
        => Assert.IsType<Outcome<Post>.Done>(await new Reply(Tids.Mint(), topic.Id, body, author, parent).Send(Cancellation)).Result;

    /// <summary>Puts the participant's place checkpoint at the log's last sequence and answers with it.</summary>
    private static async Task<long> Mark(string participantId)
    {
        var last = await Event.Log.Highest(Cancellation);
        Assert.IsType<Outcome<Checkpoint>.Done>(await new Seen(participantId, last).Send(Cancellation));
        return last;
    }

    private static async Task<Browser> SignedIn(TangentSpaceFixture tangentSpace)
    {
        var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation, handle: "reader.example")).Dispose();
        return browser;
    }

    private static async Task<HttpResponseMessage> MarkSeen(Browser browser, long through)
        => await browser.Client.PostAsync("/seen", Form(new() { [SeenController.ThroughField] = through.ToString(CultureInfo.InvariantCulture) }), Cancellation);

    private static FormUrlEncodedContent Form(Dictionary<string, string> fields) => new(fields);

    private static List<string> PostIds(JsonElement group)
        => group.GetProperty("posts").EnumerateArray().Select(item => item.GetProperty("postId").GetString()!).ToList();

    private static List<string> Shown(JsonElement topics)
        => topics.EnumerateArray().Select(shown => shown.GetProperty("topic").GetProperty("id").GetString()!).ToList();

    /// <summary>Every property name in the document, at any depth.</summary>
    private static IEnumerable<string> Names(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Names(property.Value).Prepend(property.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Names),
        _ => []
    };

    /// <summary>The two statements Koan's SQLite connector composes for the reads <see cref="LogWindow"/> makes — a
    /// field read as the dialect reads it, which is the expression its index is declared on, the order on it, then
    /// LIMIT and OFFSET; here without the row's alias, which names the same columns — and the plan SQLite gives each,
    /// as its detail lines.</summary>
    private List<(string Sql, List<string> Plan)> Plans(string database)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        var (table, seq) = Index(connection, "ix_event_seq");
        var (_, targets) = Index(connection, "ix_event_targets");
        var statements = new[]
        {
            $"SELECT * FROM \"{table}\" ORDER BY {seq[0]} DESC LIMIT {LogWindow.Size} OFFSET 0",
            $"SELECT * FROM \"{table}\" WHERE {targets[0]} = '{Reader}' AND {targets[1]} > 0 ORDER BY {seq[0]} DESC LIMIT {Unread.CountLimit + 1} OFFSET 0"
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

    private static async Task<(HttpStatusCode Status, EntityTagHeaderValue? ETag, long Through, string Body, string Caching)> Read(HttpClient client,
        EntityTagHeaderValue? held)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (held is not null) request.Headers.IfNoneMatch.Add(held);
        using var response = await client.SendAsync(request, Cancellation);
        var caching = response.Headers.CacheControl?.ToString() ?? "";
        if (response.StatusCode != HttpStatusCode.OK) return (response.StatusCode, response.Headers.ETag, 0, "", caching);
        var body = await response.Content.ReadAsStringAsync(Cancellation);
        using var json = JsonDocument.Parse(body);
        return (response.StatusCode, response.Headers.ETag, json.RootElement.GetProperty("news").GetProperty("through").GetInt64(), body, caching);
    }

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
}
