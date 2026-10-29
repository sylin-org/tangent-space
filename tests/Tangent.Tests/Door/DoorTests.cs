using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Tangents;
using Tangent.Writer;
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
    private const string Reader = "did:plc:reader";

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
        var writer = await Place(tangentSpace);
        var topic = await Started(writer, Workshop, "A question", "ada");

        var toAda = await Replied(writer, topic, "leo");
        var adaToHerself = await Replied(writer, topic, "ada");
        var toLeo = await Replied(writer, topic, "ada", parent: toAda.Id);
        var leoToHimself = await Replied(writer, topic, "leo", parent: toAda.Id);

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
        var writer = await Place(tangentSpace);
        var mine = await Started(writer, Workshop, "My question", Reader);
        var before = await Replied(writer, mine, "ada");
        var since = await Mark(writer, Reader);

        var reply = await Replied(writer, mine, "ada");
        var other = await Started(writer, Workshop, "Ada asks", "ada");
        var article = await Started(writer, Articles, "On lanterns", "leo", PostKind.Article);
        Assert.IsType<Outcome<Event>.Done>(await writer.Send(new Mentioned(Workshop, other.Id, other.Id, "ada", Reader), Cancellation));
        var own = await Replied(writer, other, Reader, body: "My own words in reply.");
        var through = await Event.HighWaterMark.Load(Cancellation);

        using var browser = await SignedIn(tangentSpace);
        using (var json = await Json(browser.Client, "/"))
        {
            var news = json.RootElement.GetProperty("news");
            Assert.Equal((since, through), (news.GetProperty("from").GetInt64(), news.GetProperty("through").GetInt64()));
            Assert.False(news.GetProperty("quiet").GetBoolean());
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
            var form = news.GetProperty("caughtUp");
            Assert.Equal(($"{BaseUrl}/", through), (form.GetProperty("action").GetString(), form.GetProperty("through").GetInt64()));
        }

        var html = await Html(browser.Client, "/");
        Assert.Contains("Since you were here: 1 reply to you, 1 mention of you, 1 new post in Articles, 2 new posts in The workshop, 1 new article.",
            WebUtility.HtmlDecode(html));
        Assert.Contains($"name=\"{DoorController.Through}\" value=\"{through.ToString(CultureInfo.InvariantCulture)}\"", html);
        Assert.Contains("Mark all seen", html);
        Assert.Contains($"href=\"{BaseUrl}/t/{Workshop}/my-question/{reply.Id}\"", html);
        Assert.DoesNotContain("My own words", html);
        Assert.DoesNotContain("<dialog", html);
    }

    [Fact]
    public async Task An_event_in_a_tangent_the_reader_may_not_view_is_in_no_group_and_a_topic_checkpoint_past_an_event_hides_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = await Place(tangentSpace);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(
            new CreateTangent("studio", "The studio", "", PostKind.Topic, 3, Audience.Private, "leo"), Cancellation));
        var mine = await Started(writer, Workshop, "My question", Reader);
        await Mark(writer, Reader);

        var first = await Replied(writer, mine, "ada");
        var second = await Replied(writer, mine, "ada");
        var secret = await Started(writer, "studio", "A private matter", "leo");
        Assert.IsType<Outcome<Event>.Done>(await writer.Send(new Mentioned("studio", secret.Id, secret.Id, "leo", Reader), Cancellation));
        Assert.IsType<Outcome<Checkpoint>.Done>(await writer.Send(
            new Store<Checkpoint>(Checkpoint.At(Reader, Scope.Topic(mine.Id), first.Sequence)), Cancellation));

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
        var writer = await Place(tangentSpace);
        var topic = await Started(writer, Workshop, "A long conversation", "ada");
        for (var index = 0; index < LogWindow.Size + 5; index++) await Replied(writer, topic, index % 2 == 0 ? "ada" : "leo");

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
        var writer = await Place(tangentSpace);
        var mine = await Started(writer, Workshop, "My question", Reader);
        await Replied(writer, mine, "ada");
        using var browser = await SignedIn(tangentSpace);

        using (var json = await Json(browser.Client, "/"))
            Assert.True(json.RootElement.GetProperty("alive")[0].GetProperty("topic").GetProperty("new").GetBoolean());
        var (status, busy, through) = await Read(browser.Client, null);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(busy, (await Read(browser.Client, null)).ETag);
        Assert.Equal(await Event.HighWaterMark.Load(Cancellation), through);

        using (var refused = await browser.Client.PostAsync("/", Form(new() { [DoorController.Through] = through.ToString(CultureInfo.InvariantCulture) }), Cancellation))
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(Reader, Scope.Place), Cancellation));

        var token = await Token(browser);
        using (var marked = await MarkSeen(browser, through, new Dictionary<string, string>(token)))
        {
            Assert.Equal(HttpStatusCode.SeeOther, marked.StatusCode);
            Assert.Equal("/", marked.Headers.Location?.OriginalString);
        }
        Assert.Equal(through, (await Checkpoint.Get(Checkpoint.Key(Reader, Scope.Place), Cancellation))!.Seq);

        var (_, quiet, _) = await Read(browser.Client, null);
        Assert.NotEqual(busy, quiet);
        Assert.Equal(HttpStatusCode.NotModified, (await Read(browser.Client, quiet)).Status);
        using (var json = await Json(browser.Client, "/"))
        {
            var news = json.RootElement.GetProperty("news");
            Assert.True(news.GetProperty("quiet").GetBoolean());
            Assert.False(news.TryGetProperty("caughtUp", out _));
            Assert.False(json.RootElement.GetProperty("alive")[0].GetProperty("topic").GetProperty("new").GetBoolean());
        }
        Assert.Contains("Nothing new since you were here.", await Html(browser.Client, "/"));

        using (var backwards = await MarkSeen(browser, through - 1, new Dictionary<string, string>(token)))
            Assert.Equal(HttpStatusCode.SeeOther, backwards.StatusCode);
        Assert.Equal(through, (await Checkpoint.Get(Checkpoint.Key(Reader, Scope.Place), Cancellation))!.Seq);

        var later = await Replied(writer, mine, "ada");
        using (var json = await Json(browser.Client, "/"))
            Assert.Equal([later.Id], PostIds(json.RootElement.GetProperty("news").GetProperty("replies")));
    }

    [Fact]
    public async Task A_visitor_on_a_public_place_reads_the_front_page_and_on_a_private_one_the_sign_in_gate()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Place(tangentSpace);
        var topic = await Started(writer, Workshop, "What should we build?", "ada");
        await Replied(writer, topic, "leo");
        var article = await Started(writer, Articles, "On lanterns", "leo", PostKind.Article);

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
        Assert.Contains("<h1 id=\"door-title\" class=\"tangent-title\">Lantern Works</h1>", html);
        Assert.Contains("The place of leo.example, @leo.example", html);
        Assert.Contains("What should we build?", html);
        Assert.Contains("On lanterns", html);
        Assert.Contains($"<a class=\"masthead-sign-in\" href=\"{BaseUrl}/sign-in?return=%2F\">Sign in</a>", html);
        Assert.DoesNotContain("Since you were here", html);

        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Audience = Audience.Private;
        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Store<TangentSpace>(space), Cancellation));

        using (var request = new HttpRequestMessage(HttpMethod.Get, "/"))
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var gated = await tangentSpace.Client.SendAsync(request, Cancellation);
            using var problem = JsonDocument.Parse(await gated.Content.ReadAsStringAsync(Cancellation));
            Assert.Equal((HttpStatusCode.Unauthorized, "application/problem+json", Capabilities.SignIn),
                (gated.StatusCode, gated.Content.Headers.ContentType?.MediaType, problem.RootElement.GetProperty("gate").GetString()));
        }

        using (var request = new HttpRequestMessage(HttpMethod.Get, "/"))
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            using var page = await tangentSpace.Client.SendAsync(request, Cancellation);
            var gatedHtml = await page.Content.ReadAsStringAsync(Cancellation);
            Assert.Equal(HttpStatusCode.Unauthorized, page.StatusCode);
            Assert.Contains("<p data-gate=\"sign-in\">sign-in</p>", gatedHtml);
            Assert.DoesNotContain("What should we build?", gatedHtml);
        }
    }

    /// <summary>The place every proof starts from: established as Lantern Works, Leo's place, with Leo an
    /// administrator, Ada and the reader members, a public discussions Tangent and a public articles Tangent.</summary>
    private static async Task<WriteLoop> Place(TangentSpaceFixture tangentSpace)
    {
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var space = await Given.AnEstablishedTangentSpace(tangentSpace, "Lantern Works", ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ada", [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        space.ProprietorId = "leo";
        space.OperatorId = "leo";
        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Store<TangentSpace>(space), Cancellation));
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(
            new CreateTangent(Workshop, "The workshop", "Where things get made.", PostKind.Topic, 2, Audience.Public, "leo"), Cancellation));
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(
            new CreateTangent(Articles, "Articles", "", PostKind.Article, 1, Audience.Public, "leo"), Cancellation));
        return writer;
    }

    private static async Task<Post> Started(WriteLoop writer, string tangent, string title, string author, PostKind kind = PostKind.Topic)
        => Assert.IsType<Outcome<Post>.Done>(await writer.Send(
            new StartTopic(Tids.Mint(), tangent, title, "The opening words of " + title + ".", author, kind), Cancellation)).Result;

    private static async Task<Post> Replied(WriteLoop writer, Post topic, string author, string? parent = null, string body = "A reply.")
        => Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(Tids.Mint(), topic.Id, body, author, parent), Cancellation)).Result;

    /// <summary>Puts the participant's place checkpoint at the log's last sequence and answers with it.</summary>
    private static async Task<long> Mark(WriteLoop writer, string participantId)
    {
        var last = await Event.HighWaterMark.Load(Cancellation);
        Assert.IsType<Outcome<Checkpoint>.Done>(await writer.Send(new Store<Checkpoint>(Checkpoint.At(participantId, Scope.Place, last)), Cancellation));
        return last;
    }

    private static async Task<Browser> SignedIn(TangentSpaceFixture tangentSpace)
    {
        var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation, handle: "reader.example")).Dispose();
        return browser;
    }

    private static async Task<Dictionary<string, string>> Token(Browser browser) => await browser.Token("/", Cancellation);

    private static async Task<HttpResponseMessage> MarkSeen(Browser browser, long through, Dictionary<string, string>? token = null)
    {
        var fields = token ?? await Token(browser);
        fields[DoorController.Through] = through.ToString(CultureInfo.InvariantCulture);
        return await browser.Client.PostAsync("/", Form(fields), Cancellation);
    }

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
            $"SELECT * FROM \"{table}\" WHERE {targets[0]} = '{Reader}' AND {targets[1]} > 0 ORDER BY {seq[0]} DESC LIMIT {LogWindow.TargetedSize} OFFSET 0"
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

    private static async Task<(HttpStatusCode Status, EntityTagHeaderValue? ETag, long Through)> Read(HttpClient client, EntityTagHeaderValue? held)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (held is not null) request.Headers.IfNoneMatch.Add(held);
        using var response = await client.SendAsync(request, Cancellation);
        if (response.StatusCode != HttpStatusCode.OK) return (response.StatusCode, response.Headers.ETag, 0);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        return (response.StatusCode, response.Headers.ETag, json.RootElement.GetProperty("news").GetProperty("through").GetInt64());
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
