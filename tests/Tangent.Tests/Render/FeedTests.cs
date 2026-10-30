using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;
using Xunit;

namespace Tangent.Render;

/// <summary>The feeds both ways: what they carry and how far, who has one and who has none, what drops out, what pages and
/// discovery say of them, and a reader polling with the tag it holds.</summary>
public sealed class FeedTests
{
    private const string BaseUrl = TangentSpaceFixture.DefaultBaseUrl;
    private const string Workshop = "the-workshop";
    private const string Back = "the-back-room";
    private const string Reader = "did:plc:reader";

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly DateTimeOffset Start = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string?> TestConnector = new()
    {
        ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
        ["Tangent:Profile:BaseAddress"] = "http://127.0.0.1:1/",
        ["Tangent:Profile:Timeout"] = "00:00:01"
    };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Both_formats_parse_as_valid_and_carry_the_topic_with_its_body_rendered_safe_and_absolute()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, "did:plc:ines", [Roles.Member], ct: Cancellation);
        await Started(writer, "A first & \u0001plain question",
            "The *words*\u0001 with <script>alert(1)</script>, [@ines](at://did:plc:ines), [a page](/about), [here](#there), ![a sketch](/media/sketch.png) and `href=\"/kept\"`.", Start);

        using var atomAnswer = await tangentSpace.Client.GetAsync($"/t/{Workshop}/feed.atom", Cancellation);
        Assert.Equal(HttpStatusCode.OK, atomAnswer.StatusCode);
        Assert.Equal("application/atom+xml", atomAnswer.Content.Headers.ContentType?.MediaType);
        var feed = XDocument.Parse(await atomAnswer.Content.ReadAsStringAsync(Cancellation)).Root!;
        Assert.Equal(Atom + "feed", feed.Name);
        Assert.Equal($"{BaseUrl}/t/{Workshop}/feed.atom", feed.Element(Atom + "id")?.Value);
        Assert.Equal("The workshop · Tangent Space", feed.Element(Atom + "title")?.Value);
        Assert.Equal("Where things get made.", feed.Element(Atom + "subtitle")?.Value);
        Assert.Equal("2026-09-28T12:00:00Z", feed.Element(Atom + "updated")?.Value);
        Assert.Contains(feed.Elements(Atom + "link"), link => (string?)link.Attribute("rel") == "self" && (string?)link.Attribute("href") == $"{BaseUrl}/t/{Workshop}/feed.atom");
        var entry = Assert.Single(feed.Elements(Atom + "entry"));
        var topic = $"{BaseUrl}/t/{Workshop}/a-first-plain-question";
        Assert.Equal(topic, entry.Element(Atom + "id")?.Value);
        Assert.Equal("A first & plain question", entry.Element(Atom + "title")?.Value);
        Assert.Equal("ada.example", entry.Element(Atom + "author")?.Element(Atom + "name")?.Value);
        Assert.Equal($"{BaseUrl}/p/ada.example", entry.Element(Atom + "author")?.Element(Atom + "uri")?.Value);
        Assert.Equal("2026-09-28T12:00:00Z", entry.Element(Atom + "published")?.Value);
        Assert.Equal("2026-09-28T12:00:00Z", entry.Element(Atom + "updated")?.Value);
        var content = entry.Element(Atom + "content")!;
        Assert.Equal("html", (string?)content.Attribute("type"));
        AssertBody(content.Value, topic);

        using var jsonAnswer = await tangentSpace.Client.GetAsync($"/t/{Workshop}/feed.json", Cancellation);
        Assert.Equal(HttpStatusCode.OK, jsonAnswer.StatusCode);
        Assert.Equal("application/feed+json", jsonAnswer.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await jsonAnswer.Content.ReadAsStringAsync(Cancellation));
        var root = json.RootElement;
        Assert.Equal("https://jsonfeed.org/version/1.1", root.GetProperty("version").GetString());
        Assert.Equal("The workshop · Tangent Space", root.GetProperty("title").GetString());
        Assert.Equal($"{BaseUrl}/t/{Workshop}", root.GetProperty("home_page_url").GetString());
        Assert.Equal($"{BaseUrl}/t/{Workshop}/feed.json", root.GetProperty("feed_url").GetString());
        var item = Assert.Single(root.GetProperty("items").EnumerateArray());
        Assert.Equal(topic, item.GetProperty("id").GetString());
        Assert.Equal(topic, item.GetProperty("url").GetString());
        Assert.Equal("ada.example", item.GetProperty("authors")[0].GetProperty("name").GetString());
        Assert.Equal(Start, item.GetProperty("date_published").GetDateTimeOffset());
        AssertBody(item.GetProperty("content_html").GetString()!, topic);
    }

    private static void AssertBody(string html, string topic)
    {
        Assert.Contains("<em>words</em>", html);
        Assert.DoesNotContain("<script", html);
        Assert.Contains($"href=\"{BaseUrl}/p/did%3Aplc%3Aines.example\">@did:plc:ines.example</a>", html);
        Assert.Contains("href=\"https://tangent.test/about\"", html);
        Assert.Contains($"href=\"{topic}#there\"", html);
        Assert.Contains("src=\"https://tangent.test/media/sketch.png\"", html);
        Assert.Contains("<code>href=", html);
        Assert.Contains("/kept", html);
        Assert.DoesNotContain("tangent.test/kept", html);
    }

    [Fact]
    public async Task A_feed_holds_a_bounded_window_of_the_newest_topics_and_neither_their_replies_nor_their_activity_move_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Place(tangentSpace);
        var all = new List<Post>();
        for (var index = 0; index < Feed.Limit + 3; index++) all.Add(await Started(writer, "Topic " + index, "Words.", Start.AddMinutes(index)));
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(Tids.Mint(), all[0].Id, "A reply.", "ada", At: Start.AddDays(1)), Cancellation));

        var entries = (await Atomic(tangentSpace.Client, $"/t/{Workshop}/feed.atom")).Elements(Atom + "entry").ToList();
        Assert.Equal(Enumerable.Range(0, Feed.Limit).Select(offset => "Topic " + (Feed.Limit + 2 - offset)), entries.Select(entry => entry.Element(Atom + "title")!.Value));

        using var json = JsonDocument.Parse(await tangentSpace.Client.GetStringAsync($"/t/{Workshop}/feed.json", Cancellation));
        Assert.Equal(Feed.Limit, json.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void The_window_is_cut_by_bytes_keeping_the_newest_always_and_never_skipping_one_to_fit_a_later_one()
    {
        static Feed.Entry Weighing(int bytes, string title) => new("https://tangent.test/x", title, new string('a', bytes), "ada", null, Start, Start);

        Assert.Equal(["huge"], Feed.Window([Weighing(Feed.Budget * 2, "huge"), Weighing(1, "small")]).Select(entry => entry.Title));
        Assert.Equal(["half", "half too"], Feed.Window([Weighing(Feed.Budget / 2 - 2048, "half"), Weighing(Feed.Budget / 2 - 2048, "half too"), Weighing(Feed.Budget, "over"), Weighing(1, "small")])
            .Select(entry => entry.Title));
        Assert.Empty(Feed.Window([]));
    }

    [Fact]
    public async Task A_members_only_tangent_has_no_feed_for_a_visitor_or_its_own_members_and_its_articles_stay_out_of_the_places()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = await Place(tangentSpace);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await Overwrites.SetAudience(roles, Back, Audience.Private, Cancellation);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(new Store<Tangents.Tangent>(Tangents.Tangent.Create(Back, "The back room", "", PostKind.Topic, 2)), Cancellation));
        await Overwrites.SetAccess(roles, Back, Roles.Member, RoleAccess.Post, Cancellation);
        var inside = Assert.IsType<Outcome<Post>.Done>(await writer.Send(
            new StartTopic(Tids.Mint(), Back, "Said in the back", "Secret words.", "ada", PostKind.Article, Start), Cancellation)).Result;
        await Started(writer, "Said in the open", "Open words.", Start.AddMinutes(1), PostKind.Article);

        using var member = new Browser(tangentSpace);
        (await member.SignIn("reader", Reader, Cancellation)).Dispose();

        foreach (var client in new[] { tangentSpace.Client, member.Client })
        {
            foreach (var path in new[] { $"/t/{Back}/feed.atom", $"/t/{Back}/feed.json", "/t/no-such-tangent/feed.atom" })
            {
                using var refused = await client.GetAsync(path, Cancellation);
                Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
            }

            var articles = await client.GetStringAsync("/articles.atom", Cancellation);
            Assert.Contains("Said in the open", articles);
            Assert.DoesNotContain(inside.Title!, articles);
            Assert.DoesNotContain("Secret words", articles);
        }

        var page = await Html(member.Client, $"/t/{Back}");
        Assert.DoesNotContain("rel=\"alternate\"", page);
        var topic = await Html(member.Client, $"/t/{Back}/{inside.Slug}");
        Assert.DoesNotContain("rel=\"alternate\"", topic);
    }

    [Fact]
    public async Task A_private_place_has_no_feeds_and_names_none()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, "Off the record", Audience.Private, ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        using var member = new Browser(tangentSpace);
        (await member.SignIn("reader", Reader, Cancellation)).Dispose();

        foreach (var path in new[] { $"/t/{Workshop}/feed.atom", $"/t/{Workshop}/feed.json", "/articles.atom", "/articles.json" })
        {
            using var visitor = await tangentSpace.Client.GetAsync(path, Cancellation);
            Assert.Equal(HttpStatusCode.Unauthorized, visitor.StatusCode);
            using var signedIn = await member.Client.GetAsync(path, Cancellation);
            Assert.Equal(HttpStatusCode.NotFound, signedIn.StatusCode);
        }

        Assert.DoesNotContain("rel=\"alternate\"", await Html(member.Client, "/"));
        Assert.DoesNotContain("rel=\"alternate\"", await Html(member.Client, $"/t/{Workshop}"));
        Assert.DoesNotContain("feed", await tangentSpace.Client.GetStringAsync("/tangent.json", Cancellation), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("feed", await tangentSpace.Client.GetStringAsync("/llms.txt", Cancellation), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_removed_topic_is_gone_from_its_tangents_feed_and_the_places_articles()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Place(tangentSpace);
        var staying = await Started(writer, "Staying", "These stay.", Start, PostKind.Article);
        var leaving = await Started(writer, "Leaving", "These go.", Start.AddMinutes(1), PostKind.Article);
        Assert.Contains("These go.", await tangentSpace.Client.GetStringAsync($"/t/{Workshop}/feed.atom", Cancellation));
        Assert.Contains("These go.", await tangentSpace.Client.GetStringAsync("/articles.json", Cancellation));

        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(leaving.Id, "leo", Start.AddMinutes(2)), Cancellation));

        foreach (var path in new[] { $"/t/{Workshop}/feed.atom", $"/t/{Workshop}/feed.json", "/articles.atom", "/articles.json" })
        {
            var text = await tangentSpace.Client.GetStringAsync(path, Cancellation);
            Assert.Contains(staying.Title!, text);
            Assert.DoesNotContain("Leaving", text);
            Assert.DoesNotContain("These go.", text);
        }
    }

    [Fact]
    public async Task An_unchanged_feed_answers_304_to_the_tag_it_gave_and_a_change_answers_anew_saying_when_and_who_edited()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Place(tangentSpace);
        var topic = await Started(writer, "A question", "The first words.", Start);

        foreach (var path in new[] { $"/t/{Workshop}/feed.atom", $"/t/{Workshop}/feed.json", "/articles.atom", "/articles.json" })
        {
            using var first = await tangentSpace.Client.GetAsync(path, Cancellation);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.NotNull(first.Headers.CacheControl?.MaxAge);
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
            using var again = await tangentSpace.Client.SendAsync(request, Cancellation);
            Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
            Assert.Empty(await again.Content.ReadAsByteArrayAsync(Cancellation));
        }

        using var held = await tangentSpace.Client.GetAsync($"/t/{Workshop}/feed.atom", Cancellation);
        var edited = Start.AddHours(1);
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new EditPost(topic.Id, topic.Version, "The second words.", null, "leo", edited), Cancellation));

        using var changed = new HttpRequestMessage(HttpMethod.Get, $"/t/{Workshop}/feed.atom");
        changed.Headers.IfNoneMatch.Add(held.Headers.ETag!);
        using var answer = await tangentSpace.Client.SendAsync(changed, Cancellation);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.NotEqual(held.Headers.ETag, answer.Headers.ETag);
        var entry = XDocument.Parse(await answer.Content.ReadAsStringAsync(Cancellation)).Root!.Element(Atom + "entry")!;
        Assert.Equal("2026-09-28T12:00:00Z", entry.Element(Atom + "published")?.Value);
        Assert.Equal("2026-09-28T13:00:00Z", entry.Element(Atom + "updated")?.Value);
        Assert.Equal("ada.example", entry.Element(Atom + "author")?.Element(Atom + "name")?.Value);
        Assert.Contains("The second words.", entry.Element(Atom + "content")?.Value);
        Assert.Contains("Edited by leo.example, 28 September 2026", entry.Element(Atom + "content")?.Value);
    }

    [Fact]
    public async Task The_articles_feed_is_a_valid_empty_feed_with_no_articles_and_carries_only_articles_once_there_are()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Started(writer, "A discussion", "Not an article.", Start);

        var empty = await Atomic(tangentSpace.Client, "/articles.atom");
        Assert.Equal("Articles · Tangent Space", empty.Element(Atom + "title")?.Value);
        Assert.Empty(empty.Elements(Atom + "entry"));
        Assert.NotNull(empty.Element(Atom + "updated"));
        using (var json = JsonDocument.Parse(await tangentSpace.Client.GetStringAsync("/articles.json", Cancellation)))
            Assert.Equal(0, json.RootElement.GetProperty("items").GetArrayLength());

        await Started(writer, "A published piece", "An article.", Start.AddMinutes(1), PostKind.Article);

        var articles = await Atomic(tangentSpace.Client, "/articles.atom");
        Assert.Equal("A published piece", Assert.Single(articles.Elements(Atom + "entry")).Element(Atom + "title")?.Value);
        Assert.Equal(2, (await Atomic(tangentSpace.Client, $"/t/{Workshop}/feed.atom")).Elements(Atom + "entry").Count());
    }

    [Fact]
    public async Task Pages_advertise_their_feeds_and_discovery_names_them()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Place(tangentSpace);
        var topic = await Started(writer, "A question", "Words.", Start);

        var door = WebUtility.HtmlDecode(await Html(tangentSpace.Client, "/"));
        Assert.Contains($"<link rel=\"alternate\" type=\"application/atom+xml\" title=\"Articles · Tangent Space (Atom)\" href=\"{BaseUrl}/articles.atom\"", door);
        Assert.Contains($"<link rel=\"alternate\" type=\"application/feed+json\" title=\"Articles · Tangent Space (JSON Feed)\" href=\"{BaseUrl}/articles.json\"", door);
        foreach (var path in new[] { $"/t/{Workshop}", $"/t/{Workshop}/{topic.Slug}" })
        {
            var page = WebUtility.HtmlDecode(await Html(tangentSpace.Client, path));
            Assert.Contains($"<link rel=\"alternate\" type=\"application/atom+xml\" title=\"The workshop · Tangent Space (Atom)\" href=\"{BaseUrl}/t/{Workshop}/feed.atom\"", page);
            Assert.Contains($"<link rel=\"alternate\" type=\"application/feed+json\" title=\"The workshop · Tangent Space (JSON Feed)\" href=\"{BaseUrl}/t/{Workshop}/feed.json\"", page);
        }

        using var descriptor = JsonDocument.Parse(await tangentSpace.Client.GetStringAsync("/tangent.json", Cancellation));
        var feeds = descriptor.RootElement.GetProperty("feeds");
        Assert.Equal($"{BaseUrl}/articles.atom", feeds.GetProperty("articles").GetProperty("atom").GetString());
        Assert.Equal($"{BaseUrl}/articles.json", feeds.GetProperty("articles").GetProperty("json").GetString());
        Assert.Equal($"{BaseUrl}/t/{{tangent}}/feed.atom", feeds.GetProperty("tangent").GetProperty("atom").GetString());
        Assert.Equal($"{BaseUrl}/t/{{tangent}}/feed.json", feeds.GetProperty("tangent").GetProperty("json").GetString());

        var llms = await tangentSpace.Client.GetStringAsync("/llms.txt", Cancellation);
        Assert.Contains($"{BaseUrl}/articles.atom", llms);
        Assert.Contains($"{BaseUrl}/t/{{tangent}}/feed.atom", llms);
    }

    private static async Task<WriteLoop> Place(TangentSpaceFixture tangentSpace)
    {
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ada", [Roles.Member], ct: Cancellation);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(
            new CreateTangent(Workshop, "The workshop", "Where things get made.", PostKind.Topic, 1, Audience.Public, "leo"), Cancellation));
        return writer;
    }

    private static async Task<Post> Started(WriteLoop writer, string title, string body, DateTimeOffset at, PostKind? kind = null)
        => Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(Tids.Mint(), Workshop, title, body, "ada", kind, at), Cancellation)).Result;

    private static async Task<XElement> Atomic(HttpClient client, string path)
        => XDocument.Parse(await client.GetStringAsync(path, Cancellation)).Root!;

    private static async Task<string> Html(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.ParseAdd("text/html");
        using var response = await client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Cancellation);
    }
}
