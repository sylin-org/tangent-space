using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Tangent.Access;
using Tangent.Events;
using Tangent.Lifecycle;
using Tangent.Participants;
using Xunit;
using static Tangent.Posts.Writing;
using F = Tangent.Space.TangentSpaceSettingsController;

namespace Tangent.Space;

/// <summary>The place's slots at <c>/settings/appearance</c>: whoever holds ManageSpace writes their own HTML, as a form or
/// as JSON, sanitized on save and bounded; the same reaches the site, an export and the archive; a hostile slot keeps
/// nothing that runs or reaches a reader; an empty one adds nothing.</summary>
public sealed class SlotsTests
{
    private const string Leo = "did:plc:leo4slots";
    private const string Path = F.AppearancePath;

    private const string Head = "<meta name=\"theme-color\" content=\"#2f6f5e\">\n<link rel=\"icon\" href=\"/media/mark.png\">";
    private const string Masthead = "<a href=\"https://sylin.org/\" class=\"brand\">Our site</a>";
    private const string DoorIntro = "<h2>Welcome</h2><p>Read <a href=\"/about\">about us</a>.</p>";
    private const string ArticleHeader = "<p>From the long reads.</p>";
    private const string Footer = "<a href=\"https://sylin.org/rules\">Rules</a>";

    private static Dictionary<string, string> All() => new()
    {
        [F.HeadField] = Head,
        [F.MastheadField] = Masthead,
        [F.DoorIntroField] = DoorIntro,
        [F.ArticleHeaderField] = ArticleHeader,
        [F.FooterField] = Footer
    };

    private static async Task<Slots> Held() => (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Slots;

    private static string HeadOf(string page) => page[..page.IndexOf("</head>", StringComparison.Ordinal)];

    private static string Slot(string name, string html) => $"<div class=\"slot slot-{name}\">{html}</div>";

    private static string ProseSlot(string name, string html) => $"<div class=\"slot slot-{name} post-body\">{html}</div>";

    private static void AssertCarries(string page, bool door = false, bool article = false)
    {
        Assert.Contains(Head, HeadOf(page));
        Assert.Contains(Slot("masthead", Masthead), page);
        Assert.Contains(Slot("footer", Footer), page);
        if (door) Assert.Contains("Welcome</h2>", page);
        Assert.Equal(article, page.Contains(ProseSlot("article-header", ArticleHeader), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Whoever_holds_manage_space_writes_the_slots_as_a_form_or_as_json_and_every_page_for_every_reader_carries_them()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var visitor = Agent(tangentSpace);
        var topic = await Started(writer, Ada, "A plain topic", "Its words.");
        var article = await Started(writer, Ada, "A long read", "Its words.", Articles);
        var held = await Tagged(visitor, "/t/" + Workshop, null);
        var before = await Event.Count.Exact(Cancellation);

        var saved = await Form(leo, Path, Path, All());

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + Path), (saved.Status, saved.Location));
        var logged = Assert.Single(await Event.All(Cancellation), e => e.Seq > before);
        Assert.Equal((EventKind.SpaceChanged, Leo), (logged.Kind, logged.ActorId));

        // The door, a Topic and an article, for a visitor and a participant; the article header heads an article alone, and
        // the door's intro reaches the fragment that refreshes it and its JSON.
        foreach (var client in new[] { visitor, leo.Client })
        {
            AssertCarries((await Send(client, HttpMethod.Get, "/")).Body, door: true);
            AssertCarries((await Send(client, HttpMethod.Get, TopicPath(topic))).Body);
            AssertCarries((await Send(client, HttpMethod.Get, TopicPath(article))).Body, article: true);
        }

        Assert.Contains(ProseSlot("door-intro", (await Held()).DoorIntro), (await Send(visitor, HttpMethod.Get, "/")).Body);
        Assert.Contains("post-body", await Fragment(visitor, "/"));
        Assert.Equal((await Held()).DoorIntro, (await Send(visitor, HttpMethod.Get, "/", json: true)).Json.GetProperty("intro").GetString());
        Assert.Equal(ArticleHeader, (await Send(visitor, HttpMethod.Get, TopicPath(article), json: true)).Json.GetProperty("articleHeader").GetString());

        // A page a cache already holds no longer answers for them.
        Assert.Equal(HttpStatusCode.OK, (await Tagged(visitor, "/t/" + Workshop, held.ETag)).Status);

        // The form opens holding them, and reads back in JSON.
        var page = await Send(leo.Client, HttpMethod.Get, Path, json: true);
        Assert.Equal(Masthead, page.Json.GetProperty("slots").GetProperty("masthead").GetString());
        Assert.Equal(Head, page.Json.GetProperty("slots").GetProperty("head").GetString());
        Assert.Contains(WebUtility.HtmlEncode(Masthead), (await Send(leo.Client, HttpMethod.Get, Path)).Body.Replace("&#x27;", "'"));

        // Sent back as it stands, with a browser's line endings, nothing changes and nothing is written.
        var count = await Event.Count.Exact(Cancellation);
        var again = All();
        again[F.HeadField] = (await Held()).Head.Replace("\n", "\r\n");
        again[F.DoorIntroField] = (await Held()).DoorIntro;
        Assert.Equal(HttpStatusCode.SeeOther, (await Form(leo, Path, Path, again)).Status);
        Assert.Equal(count, await Event.Count.Exact(Cancellation));

        // As JSON: one slot changes, the others stay, and the answer reads them.
        var written = await Json(leo, Path, Path, new { footer = "<em>Thanks</em>" });
        Assert.Equal((HttpStatusCode.OK, "<em>Thanks</em>"), (written.Status, written.Json.GetProperty("slots").GetProperty("footer").GetString()));
        Assert.Equal(Masthead, (await Held()).Masthead);
        Assert.Equal(count + 1, await Event.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task A_hostile_slot_keeps_nothing_that_runs_or_reaches_a_reader()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var visitor = Agent(tangentSpace);

        const string hostile = "<script>alert(1)</script>"
            + "<img src=\"https://cdn.example/x.png\" onerror=\"alert(2)\" alt=\"kept\">"
            + "<a href=\"javascript:alert(3)\">a link</a>"
            + "<a href=\"data:text/html,alert(4)\">another</a>"
            + "<p onclick=\"alert(5)\" style=\"position:fixed\" id=\"masthead\" data-island=\"fragment\" data-props=\"{}\">words</p>"
            + "<form action=\"https://evil.example\"><input name=\"token\"><button>Go</button></form>"
            + "<iframe src=\"https://evil.example\"></iframe><svg onload=\"alert(6)\"></svg><object data=\"x\"></object><base href=\"https://evil.example/\">";
        foreach (var field in new[] { F.MastheadField, F.DoorIntroField, F.ArticleHeaderField, F.FooterField })
            Assert.Equal(HttpStatusCode.OK, (await Json(leo, Path, Path, new Dictionary<string, string> { [field] = hostile })).Status);

        var slots = await Held();
        foreach (var kept in new[] { slots.Masthead, slots.DoorIntro, slots.ArticleHeader, slots.Footer })
        {
            foreach (var gone in new[] { "script", "alert", "onerror", "onclick", "onload", "javascript", "data:", "data-", "style=", "id=", "<form", "<input", "<button", "<iframe", "<svg", "<object", "<base", "evil.example" })
                Assert.DoesNotContain(gone, kept, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("alt=\"kept\"", kept);
            Assert.Contains("<p>words</p>", kept);
            Assert.Contains("no-referrer", kept);
        }

        // A head keeps what a head needs, and a name its meta alone may have.
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, Path, Path, new
        {
            head = "<script>alert(1)</script><base href=\"https://evil.example/\"><title>Taken</title>"
                + "<meta http-equiv=\"refresh\" content=\"0; url=https://evil.example\">"
                + "<meta http-equiv=\"Set-Cookie\" content=\"a=b\">"
                + "<meta name=\"referrer\" content=\"unsafe-url\"><meta charset=\"utf-7\">"
                + "<meta name=\"keep-me\" content=\"yes\">"
                + "<link rel=\"stylesheet\" href=\"https://evil.example/s.css\">"
                + "<link rel=\"icon stylesheet\" href=\"https://evil.example/t.css\">"
                + "<link rel=\"preload\" as=\"script\" href=\"https://evil.example/s.js\">"
                + "<link rel=\"icon\" href=\"javascript:alert(2)\">"
                + "<link rel=\"icon\" href=\"https://cdn.example/mark.png\">"
                + "stray words<div>and more</div><img src=\"https://evil.example/i.png\">"
        })).Status);
        var head = (await Held()).Head;
        Assert.Equal("<meta name=\"keep-me\" content=\"yes\"><link rel=\"icon\" href=\"https://cdn.example/mark.png\">", head);
        Assert.Contains(head, HeadOf((await Send(visitor, HttpMethod.Get, "/")).Body));

        // No slot holds CSS, however harmless it reads: a selector and keywords alone can send a form's token away through a
        // lazy image.
        const string css = "<style>input[value^=a]~.post-form-actions button{anchor-name:--a}.leak-a{position-anchor:--a;position-area:bottom}</style>";
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, Path, Path, new { head = css, masthead = css, doorIntro = css, articleHeader = css, footer = css })).Status);
        Assert.Equal(new Slots(), await Held());
    }

    [Fact]
    public async Task An_image_or_an_icon_on_the_places_own_origin_stays_only_under_media()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);

        foreach (var address in new[]
                 {
                     "/auth/logout", "/notifications", "/media", "/media/", "/mediafile", "/MEDIA/x.png", "auth/logout", "media/x.png", "./media/x.png", "//sylin.org/x.png",
                     "/media/../auth/logout", "/media/%2e%2e/auth/logout", "/media/%2E%2E%2Fauth/logout", "/media%2f..%2fauth/logout", "/media/%252e%252e/auth/logout",
                     "/media/..\\auth/logout", "/media/%5c..%5cauth/logout", "/media/./x.png", "/auth/logout?x=/media/y"
                 })
        {
            Assert.Equal(HttpStatusCode.OK, (await Json(leo, Path, Path, new
            {
                head = $"<link rel=\"icon\" href=\"{address}\"><link rel=\"apple-touch-icon\" href=\"{address}\">",
                masthead = $"<img src=\"{address}\" alt=\"gone\">",
                doorIntro = $"<p>words<img src=\"{address}\"></p>",
                articleHeader = $"<img src=\"{address}\">",
                footer = $"<img src=\"{address}\">"
            })).Status);
            var gone = await Held();
            Assert.Equal(new Slots { DoorIntro = "<p>words</p>" }, gone);
        }

        // Under /media/ it stays, as does one on another host, and rel="me" names a page no browser loads.
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, Path, Path, new
        {
            head = "<link rel=\"icon\" href=\"/media/mark.png?v=2\"><link rel=\"shortcut icon\" href=\"https://cdn.example/mark.png\"><link rel=\"me\" href=\"/p/ada\">",
            footer = "<img src=\"/media/0123abcd\"><img src=\"https://cdn.example/x.png\">"
        })).Status);
        var kept = await Held();
        Assert.Equal("<link rel=\"icon\" href=\"/media/mark.png?v=2\"><link rel=\"shortcut icon\" href=\"https://cdn.example/mark.png\"><link rel=\"me\" href=\"/p/ada\">", kept.Head);
        Assert.Contains("src=\"/media/0123abcd\"", kept.Footer);
        Assert.Contains("src=\"https://cdn.example/x.png\"", kept.Footer);
    }

    [Fact]
    public async Task A_slot_over_its_bound_is_refused_and_what_was_written_stays_in_the_form()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        var before = await Event.Count.Exact(Cancellation);

        foreach (var (field, limit, rule) in new[]
                 {
                     (F.MastheadField, Slots.MastheadLimit, Slots.MastheadRule),
                     (F.DoorIntroField, Slots.DoorIntroLimit, Slots.DoorIntroRule),
                     (F.ArticleHeaderField, Slots.ArticleHeaderLimit, Slots.ArticleHeaderRule),
                     (F.FooterField, Slots.FooterLimit, Slots.FooterRule)
                 })
        {
            var over = new string('x', limit + 1);
            var json = await Json(leo, Path, Path, new Dictionary<string, string> { [field] = over });
            Assert.Equal(HttpStatusCode.BadRequest, json.Status);
            Assert.Contains(rule, json.Body);
            var form = await Form(leo, Path, Path, new Dictionary<string, string> { [field] = over, [F.AccentField] = "jade" });
            Assert.Equal(HttpStatusCode.BadRequest, form.Status);
            Assert.Contains("id=\"space-appearance-failure\"", form.Body);
            Assert.Contains(rule, form.Body);
            Assert.Contains(over, form.Body);
        }

        var head = "<meta name=\"long\" content=\"" + new string('x', Slots.HeadLimit) + "\">";
        var refused = await Json(leo, Path, Path, new { head });
        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Contains(Slots.HeadRule, refused.Body);

        Assert.Equal(new Slots(), await Held());
        Assert.Equal(before, await Event.Count.Exact(Cancellation));

        // At its bound a slot is written.
        Assert.Equal(HttpStatusCode.OK, (await Json(leo, Path, Path, new { footer = new string('x', Slots.FooterLimit) })).Status);
    }

    [Fact]
    public async Task Anyone_who_does_not_hold_manage_space_is_refused_and_the_slots_stay()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var visitor = Agent(tangentSpace);
        var gate = Capabilities.NoRoleGrants(Bit.ManageSpace);
        var before = await Event.Count.Exact(Cancellation);

        var form = await Form(ada, "/t/" + Workshop, Path, All());
        Assert.Equal(HttpStatusCode.Forbidden, form.Status);
        Assert.Contains($"data-gate=\"{gate}\"", form.Body);
        Assert.DoesNotContain("type=\"submit\"", form.Body);
        var json = await Json(ada, "/t/" + Workshop, Path, new { footer = Footer });
        Assert.Equal((HttpStatusCode.Forbidden, gate), (json.Status, json.Gate));
        var posted = await Send(visitor, HttpMethod.Post, Path, JsonBody(new { footer = Footer }), json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn), (posted.Status, posted.Gate));

        Assert.Equal(new Slots(), await Held());
        Assert.Equal(before, await Event.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task An_empty_slot_adds_nothing_to_a_page()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var visitor = Agent(tangentSpace);
        var article = await Started(writer, Ada, "A long read", "Its words.", Articles);
        var door = Unpositioned((await Send(visitor, HttpMethod.Get, "/")).Body);
        var topic = Unpositioned((await Send(visitor, HttpMethod.Get, TopicPath(article))).Body);
        Assert.DoesNotContain("class=\"slot", door);
        Assert.DoesNotContain("class=\"slot", topic);
        Assert.False((await Send(visitor, HttpMethod.Get, "/", json: true)).Json.TryGetProperty("intro", out _));
        Assert.False((await Send(visitor, HttpMethod.Get, TopicPath(article), json: true)).Json.TryGetProperty("articleHeader", out _));

        // Written, then emptied, what a page holds is what it held; what the sanitizer takes whole leaves a slot empty too.
        Assert.Equal(HttpStatusCode.SeeOther, (await Form(leo, Path, Path, All())).Status);
        Assert.NotEqual(door, Unpositioned((await Send(visitor, HttpMethod.Get, "/")).Body));
        var emptied = All().ToDictionary(field => field.Key, _ => "<script>alert(1)</script>  ");
        Assert.Equal(HttpStatusCode.SeeOther, (await Form(leo, Path, Path, emptied)).Status);

        Assert.Equal(new Slots(), await Held());
        Assert.Equal(door, Unpositioned((await Send(visitor, HttpMethod.Get, "/")).Body));
        Assert.Equal(topic, Unpositioned((await Send(visitor, HttpMethod.Get, TopicPath(article))).Body));
    }

    [Fact]
    public async Task An_export_and_an_archive_carry_the_slots_as_the_site_does()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        var article = await Started(writer, Ada, "A long read", "Its words.", Articles);
        var topic = await Started(writer, Ada, "A plain topic", "Its words.");
        Assert.Equal(HttpStatusCode.SeeOther, (await Form(leo, Path, Path, All())).Status);

        // A page standing alone names the place for what starts at its root, and is otherwise what the site says.
        var rootedHead = Head.Replace("href=\"/media/mark.png\"", $"href=\"{BaseUrl}/media/mark.png\"");
        void AssertAlone(string page, bool door, bool isArticle)
        {
            Assert.Contains(rootedHead, HeadOf(page));
            Assert.Contains(Slot("masthead", Masthead), page);
            Assert.Contains(Slot("footer", Footer), page);
            Assert.Equal(isArticle, page.Contains(ProseSlot("article-header", ArticleHeader), StringComparison.Ordinal));
            Assert.Equal(door, page.Contains($"<a href=\"{BaseUrl}/about\">about us</a>", StringComparison.Ordinal));
        }

        using var answer = await leo.Client.GetAsync(ExportController.Path + ".zip", Cancellation);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        await using var bytes = await answer.Content.ReadAsStreamAsync(Cancellation);
        using var zip = new ZipArchive(bytes, ZipArchiveMode.Read);
        string Zipped(string name)
        {
            using var reader = new StreamReader(zip.GetEntry(name)!.Open());
            return reader.ReadToEnd();
        }

        AssertAlone(Zipped("index.html"), door: false, isArticle: false);
        AssertAlone(Zipped($"t/{Articles}/{article.Slug}/index.html"), door: false, isArticle: true);
        AssertAlone(Zipped($"t/{Workshop}/{topic.Slug}/index.html"), door: false, isArticle: false);

        var folder = System.IO.Path.Combine(tangentSpace.Folder, "archive");
        Assert.Equal(BaseUrl, await Archive.Write(folder, tangentSpace.Services, Cancellation));
        var archivedDoor = await File.ReadAllTextAsync(System.IO.Path.Combine(folder, "index.html"), Cancellation);
        AssertAlone(archivedDoor, door: true, isArticle: false);
        Assert.Contains("<p class=\"retired-line\">This place is retired.", archivedDoor);
        AssertAlone(await File.ReadAllTextAsync(System.IO.Path.Combine(folder, "t", Articles, article.Slug!, "index.html"), Cancellation), door: false, isArticle: true);
        AssertAlone(await File.ReadAllTextAsync(System.IO.Path.Combine(folder, "t", Workshop, topic.Slug!, "index.html"), Cancellation), door: false, isArticle: false);
    }

    /// <summary>A page without where its live updater resumes, which each write moves.</summary>
    private static string Unpositioned(string page) => Regex.Replace(page,@"&quot;since&quot;:\d+", "");

    /// <summary>A page as the fragment island asks for it, its component alone.</summary>
    private static async Task<string> Fragment(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Add(Render.HtmlFormatter.FragmentHeader, "1");
        using var response = await client.SendAsync(request, Cancellation);
        return await response.Content.ReadAsStringAsync(Cancellation);
    }

    /// <summary>A page as a browser that holds <paramref name="held"/> revalidates it.</summary>
    private static async Task<(HttpStatusCode Status, EntityTagHeaderValue? ETag)> Tagged(HttpClient client, string path, EntityTagHeaderValue? held)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        if (held is not null) request.Headers.IfNoneMatch.Add(held);
        using var response = await client.SendAsync(request, Cancellation);
        return (response.StatusCode, response.Headers.ETag);
    }
}
