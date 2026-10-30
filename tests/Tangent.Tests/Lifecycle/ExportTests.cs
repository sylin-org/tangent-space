using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Lifecycle;

/// <summary>Export at <c>/settings/export</c>, one zip of readable pages beside their data: what it opens as, how far it
/// walks, what it leaves out, who it refuses, and that a tombstone stays one.</summary>
public sealed partial class ExportTests
{
    private const string Leo = "did:plc:leo4export";
    private const string Path = ExportController.Path;

    [Fact]
    public async Task A_tangents_export_opens_as_linked_html_with_its_data_and_answers_as_it_is_written()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        var root = await Started(writer, Ada, "How do you sharpen a card scraper",
            $"Burnish it, says [@bea](at://{Bea}), then see [the notes](/about) and ![the bench](/media/bench.png).");
        var reply = await Replied(writer, root, Bea, "A little oil on the *burnisher*.");
        var taken = await Taken(writer, reply, Articles, "On oil");

        using var answer = await leo.Client.GetAsync(Path + "/" + Workshop + ".zip", HttpCompletionOption.ResponseHeadersRead, Cancellation);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("application/zip", answer.Content.Headers.ContentType?.MediaType);
        Assert.Null(answer.Content.Headers.ContentLength);
        Assert.Matches("^field-notes-workshop-\\d{4}-\\d{2}-\\d{2}\\.zip$", answer.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        var zip = await Unzipped(answer);

        // It holds that Tangent's Topics, and no other Tangent's.
        Assert.Contains("t/workshop/how-do-you-sharpen-a-card-scraper/index.html", zip.Keys);
        Assert.DoesNotContain(zip.Keys, name => name.StartsWith("t/" + Articles, StringComparison.Ordinal) || name.StartsWith("t/" + Studio, StringComparison.Ordinal));
        AssertOpensAlone(zip);

        // A page reads as its page does: the door names the one Tangent, the Tangent its Topic, and the Topic its words.
        Assert.Contains("href=\"t/workshop/index.html\"", zip["index.html"]);
        Assert.Contains("href=\"how-do-you-sharpen-a-card-scraper/index.html\"", zip["t/workshop/index.html"]);
        var page = zip["t/workshop/how-do-you-sharpen-a-card-scraper/index.html"];
        Assert.Contains("How do you sharpen a card scraper", page);
        Assert.Contains("<em>burnisher</em>", page);
        Assert.Contains("href=\"../../../tangent.css\"", page);
        Assert.Contains("<html lang=\"en\" data-accent=\"gold\">", page);
        Assert.Contains($"id=\"{reply.Id}\"", page);

        // A body's root-relative targets and a profile name the place, which they are of; a mention still reads.
        Assert.Contains($"href=\"{BaseUrl}/about\"", page);
        Assert.Contains($"src=\"{BaseUrl}/media/bench.png\"", page);
        Assert.Contains($"href=\"{ProfilePage.AddressOf(BaseUrl, Bea + ".example")}\"", page);

        // A Topic taken into a Tangent this zip leaves out is the live address.
        Assert.Contains($"href=\"{BaseUrl}/t/{Articles}/{taken.Slug}\"", page);

        // Its data is what the page says, as the page's JSON says it, beside it.
        var data = JsonDocument.Parse(zip["t/workshop/how-do-you-sharpen-a-card-scraper/index.json"]).RootElement;
        Assert.Equal(root.Id, data.GetProperty("id").GetString());
        Assert.Equal(Ada, data.GetProperty("root").GetProperty("author").GetProperty("id").GetString());
        Assert.Contains("Burnish it", data.GetProperty("root").GetProperty("body").GetString());
        var replied = Assert.Single(data.GetProperty("replies").EnumerateArray());
        Assert.Equal((reply.Id, "A little oil on the *burnisher*."), (replied.GetProperty("id").GetString(), replied.GetProperty("body").GetString()));
        Assert.Equal(root.Id, JsonDocument.Parse(zip["t/workshop/index.json"]).RootElement.GetProperty("topics")[0].GetProperty("id").GetString());
        Assert.Equal(Workshop, JsonDocument.Parse(zip["index.json"]).RootElement.GetProperty("tangents")[0].GetProperty("slug").GetString());
    }

    [Fact]
    public async Task The_whole_place_is_walked_across_every_read_page_and_each_link_lands_within_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);

        // More Tangents than a page of them lists, more Topics than a page of a Tangent lists, and more replies than a
        // window of a Topic holds, one of them answering a reply a window back.
        foreach (var number in Enumerable.Range(1, TangentsController.PageSize))
            await Given.ATangent(tangentSpace, "more-" + number, ct: Cancellation);
        foreach (var number in Enumerable.Range(1, TopicList.PageSize))
            await Started(writer, Ada, "Topic number " + number, "Words of topic " + number);
        var long_ = await Started(writer, Ada, "A long one", "It goes on.");
        var replies = new List<Post>();
        foreach (var number in Enumerable.Range(1, Export.WindowSize + 1))
            replies.Add(await Replied(writer, long_, Bea, "Reply number " + number));
        var answering = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(Tids.Mint(), long_.Id, "Back to the first", Ada, replies[0].Id), Cancellation)).Result;
        var taken = await Taken(writer, replies[^1], Articles, "From the end of a long one");

        var zip = await Downloaded(leo, Path + ".zip");

        AssertOpensAlone(zip);
        Assert.Contains("page-2.html", zip.Keys);
        Assert.Contains("href=\"page-2.html\"", zip["index.html"]);
        Assert.Contains("t/more-1/index.html", zip.Keys);
        Assert.Contains($"t/more-{TangentsController.PageSize}/index.html", zip.Keys);
        Assert.Contains("t/workshop/page-2.html", zip.Keys);
        foreach (var number in Enumerable.Range(1, TopicList.PageSize))
            Assert.Contains($"t/workshop/topic-number-{number}/index.html", zip.Keys);

        // Every reply is in exactly one window, and the two windows lead to each other.
        var first = zip["t/workshop/a-long-one/index.json"];
        var second = zip["t/workshop/a-long-one/2.json"];
        var held = new[] { first, second }.SelectMany(file => JsonDocument.Parse(file).RootElement.GetProperty("replies").EnumerateArray())
            .Select(reply => reply.GetProperty("id").GetString()).ToList();
        Assert.Equal(replies.Select(reply => reply.Id).Append(answering.Id), held);
        Assert.Contains("href=\"2.html\"", zip["t/workshop/a-long-one/index.html"]);
        Assert.Contains("href=\"index.html\"", zip["t/workshop/a-long-one/2.html"]);

        // A reply's way back reaches its parent's window, and a Topic taken on a tangent reaches where it was taken from.
        Assert.Contains($"href=\"index.html#{replies[0].Id}\"", zip["t/workshop/a-long-one/2.html"]);
        Assert.Contains($"href=\"../../{Workshop}/a-long-one/2.html#{replies[^1].Id}\"", zip[$"t/{Articles}/{taken.Slug}/index.html"]);
        Assert.Contains($"href=\"../../{Articles}/{taken.Slug}/index.html\"", zip["t/workshop/a-long-one/2.html"]);

        // The page says where each download is, and pages through the Tangents as the place's list does.
        var page = await Send(leo.Client, HttpMethod.Get, Path, json: true);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(BaseUrl + Path + ".zip", page.Json.GetProperty("whole").GetString());
        Assert.Equal(TangentsController.PageSize, page.Json.GetProperty("tangents").GetArrayLength());
        var tangent = page.Json.GetProperty("tangents")[0];
        Assert.Equal(BaseUrl + Path + "/" + tangent.GetProperty("slug").GetString() + ".zip", tangent.GetProperty("address").GetString());
        var more = page.Json.GetProperty("next").GetString()!;
        var next = await Send(leo.Client, HttpMethod.Get, more[BaseUrl.Length..], json: true);
        Assert.True(next.Json.GetProperty("continued").GetBoolean());
        Assert.NotEmpty(next.Json.GetProperty("tangents").EnumerateArray());
        Assert.False(page.Json.GetProperty("tangents").EnumerateArray().Select(row => row.GetProperty("slug").GetString())
            .Intersect(next.Json.GetProperty("tangents").EnumerateArray().Select(row => row.GetProperty("slug").GetString())).Any());
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(leo.Client, HttpMethod.Get, Path + "?after=nope", json: true)).Status);
    }

    [Fact]
    public async Task It_carries_the_members_only_tangents_its_reader_may_view_and_no_tangent_they_may_not()
    {
        const string members = "the-members";
        const string keeper = "role:keeper";
        const string Keeping = "did:plc:keeper4export";
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await roles.Define(keeper, "Keeper", Bits.Tokens(Bit.ManageSpace), ct: Cancellation);
        await Given.ATangent(tangentSpace, members, Audience.Private, ct: Cancellation);
        await Overwrites.SetAccess(roles, members, Roles.Member, RoleAccess.Post, Cancellation);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Keeping, [Roles.Member, keeper], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var keeping = await SignedIn(tangentSpace, Keeping);
        await Started(writer, Ada, "For the members", "Words the members read", members);
        var open = await Started(writer, Ada, "In the open", "Words anyone reads");
        var secret = await Started(writer, Leo, "In the studio", "Words only the studio reads", Studio);
        await Taken(writer, open, Studio, "Taken into the studio");

        var zip = await Downloaded(keeping, Path + ".zip");

        AssertOpensAlone(zip);
        Assert.Contains($"t/{members}/for-the-members/index.html", zip.Keys);
        Assert.Contains($"t/{Workshop}/in-the-open/index.html", zip.Keys);
        Assert.DoesNotContain(zip.Keys, name => name.StartsWith("t/" + Studio, StringComparison.Ordinal));
        Assert.DoesNotContain(zip, file => file.Value.Contains("only the studio reads") || file.Value.Contains("Taken into the studio") || file.Value.Contains(secret.Id));

        // The page lists what they may take, and the Tangent they may not view is an address with nothing there.
        var page = await Send(keeping.Client, HttpMethod.Get, Path, json: true);
        Assert.Contains(members, page.Json.GetProperty("tangents").EnumerateArray().Select(row => row.GetProperty("slug").GetString()));
        Assert.DoesNotContain(Studio, page.Json.GetProperty("tangents").EnumerateArray().Select(row => row.GetProperty("slug").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(keeping.Client, HttpMethod.Get, Path + "/" + Studio + ".zip")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(keeping.Client, HttpMethod.Get, Path + "/no-such-tangent.zip")).Status);

        // Whoever may view it takes it along too.
        Assert.Contains($"t/{Studio}/in-the-studio/index.html", (await Downloaded(leo, Path + ".zip")).Keys);
        Assert.Contains($"t/{Studio}/in-the-studio/index.html", (await Downloaded(leo, Path + "/" + Studio + ".zip")).Keys);
    }

    [Fact]
    public async Task A_reader_without_manage_space_is_refused_the_page_and_every_zip()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var visitor = Agent(tangentSpace);

        var refused = await Send(ada.Client, HttpMethod.Get, Path, json: true);
        Assert.Equal(HttpStatusCode.Forbidden, refused.Status);
        Assert.False(refused.Json.TryGetProperty("whole", out _));
        Assert.Equal(0, refused.Json.GetProperty("tangents").GetArrayLength());
        Assert.False(string.IsNullOrEmpty(refused.Json.GetProperty("gate").GetString()));
        var refusedPage = await Send(ada.Client, HttpMethod.Get, Path);
        Assert.Equal(HttpStatusCode.Forbidden, refusedPage.Status);
        Assert.DoesNotContain(".zip", refusedPage.Body);
        foreach (var zip in new[] { Path + ".zip", Path + "/" + Workshop + ".zip" })
        {
            var answer = await Send(ada.Client, HttpMethod.Get, zip, json: true);
            Assert.Equal(HttpStatusCode.Forbidden, answer.Status);
            Assert.Equal("application/problem+json", answer.MediaType);
            var asked = await Send(visitor, HttpMethod.Get, zip, json: true);
            Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn), (asked.Status, asked.Gate));
        }

        var visiting = await Send(visitor, HttpMethod.Get, Path);
        Assert.Equal(HttpStatusCode.Unauthorized, visiting.Status);
        Assert.Contains("/sign-in?return=", visiting.Body);

        // Whoever holds it finds the section in the rail, with a link that needs no script.
        var page = await Send(leo.Client, HttpMethod.Get, Path);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains($"href=\"{BaseUrl}{Path}\" aria-current=\"page\"", page.Body);
        Assert.Contains($"href=\"{BaseUrl}{Path}.zip\" download", page.Body);
        Assert.Contains($"href=\"{BaseUrl}{Path}/{Workshop}.zip\" download", page.Body);
        Assert.DoesNotContain("Export", (await Send(ada.Client, HttpMethod.Get, TangentSpaceSettingsController.Path, json: true)).Body);
    }

    [Fact]
    public async Task A_removed_post_stays_a_tombstone_with_nothing_of_its_words_anywhere()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        var root = await Started(writer, Ada, "A topic that stands", "Words that stand.");
        var kept = await Replied(writer, root, Bea, "A reply that stands.");
        var gone = await Replied(writer, root, Bea, "Words nobody keeps.");
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(gone.Id, Mo), Cancellation));
        var edited = await Started(writer, Ada, "A topic removed whole", "Words of a removed root.");
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(edited.Id, Ada), Cancellation));

        var zip = await Downloaded(leo, Path + ".zip");

        Assert.DoesNotContain(zip, file => file.Value.Contains("Words nobody keeps") || file.Value.Contains("Words of a removed root"));
        var page = zip["t/workshop/a-topic-that-stands/index.html"];
        Assert.Contains($"id=\"{gone.Id}\"", page);
        Assert.Contains("post-removed", page);
        Assert.Contains("Removed by", page);
        Assert.Contains("A reply that stands.", page);
        var data = JsonDocument.Parse(zip["t/workshop/a-topic-that-stands/index.json"]).RootElement.GetProperty("replies");
        var tombstone = data.EnumerateArray().Single(reply => reply.GetProperty("id").GetString() == gone.Id);
        Assert.False(tombstone.TryGetProperty("body", out _));
        Assert.Equal(Mo, tombstone.GetProperty("removed").GetProperty("by").GetProperty("id").GetString());
        Assert.Equal(kept.Id, data[0].GetProperty("id").GetString());

        // A removed root keeps its Topic's place too.
        Assert.Contains("t/workshop/a-topic-removed-whole/index.html", zip.Keys);
        Assert.Contains("post-removed", zip["t/workshop/a-topic-removed-whole/index.html"]);
        AssertOpensAlone(zip);
    }

    [Fact]
    public async Task Under_a_path_its_pages_still_open_alone_and_name_the_place_by_its_origin()
    {
        var settings = Settings();
        settings["Koan:Web:ApplicationUrl"] = "https://tangent.example/notes";
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(settings, ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = new Browser(tangentSpace);
        (await leo.SignIn("leo", Leo, Cancellation, handle: "leo.example", under: "/notes")).Dispose();
        await Started(writer, Ada, "Beside a path", "See [the notes](/notes/about).");

        var zip = await Downloaded(leo, "/notes" + Path + ".zip");

        AssertOpensAlone(zip);
        Assert.Contains("href=\"https://tangent.example/notes/about\"", zip["t/workshop/beside-a-path/index.html"]);
    }

    private static async Task<Post> Taken(WriteLoop writer, Post origin, string tangent, string title)
        => Assert.IsType<Outcome<Post>.Done>(await writer.Send(
            new StartTopic(Tids.Mint(), tangent, title, "", Leo, OriginId: origin.Id), Cancellation)).Result;

    /// <summary>The zip at <paramref name="path"/>, every file read as text but the fonts.</summary>
    private static async Task<Dictionary<string, string>> Downloaded(Browser browser, string path)
    {
        using var answer = await browser.Client.GetAsync(path, Cancellation);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        return await Unzipped(answer);
    }

    private static async Task<Dictionary<string, string>> Unzipped(HttpResponseMessage answer)
    {
        await using var bytes = new MemoryStream(await answer.Content.ReadAsByteArrayAsync(Cancellation));
        using var zip = new ZipArchive(bytes, ZipArchiveMode.Read);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            Assert.True(files.TryAdd(entry.FullName, reader.ReadToEnd()), entry.FullName + " is written twice.");
        }

        return files;
    }

    /// <summary>Every page opens with no server: no script, no form, no island, and each link and image that is not
    /// absolute lands on a file the zip holds, at a place that file holds; and the stylesheet's fonts are beside it.</summary>
    private static void AssertOpensAlone(IReadOnlyDictionary<string, string> zip)
    {
        // No page offers what needs the server: a search has nowhere to go.
        Assert.DoesNotContain(zip, file => file.Key.EndsWith(".html", StringComparison.Ordinal) && file.Value.Contains("masthead-search"));
        Assert.Contains("index.html", zip.Keys);
        foreach (var (name, content) in zip.Where(file => file.Key.EndsWith(".html", StringComparison.Ordinal)))
        {
            Assert.Contains(name[..^".html".Length] + ".json", zip.Keys);
            Assert.DoesNotContain("<script", content);
            Assert.DoesNotContain("<form", content);
            Assert.DoesNotContain("data-island", content);
            foreach (Match target in Target().Matches(content))
            {
                var address = WebUtility.HtmlDecode(target.Groups["address"].Value);
                if (address.StartsWith("https://", StringComparison.Ordinal) || address.StartsWith("http://", StringComparison.Ordinal)) continue;
                Assert.False(address.StartsWith('/'), $"{name} links {address}, which leads off the zip.");
                var parts = address.Split('#');
                var file = parts[0].Length == 0 ? name : Resolved(name, Uri.UnescapeDataString(parts[0]));
                Assert.True(zip.ContainsKey(file), $"{name} links {address}, and the zip holds no {file}.");
                if (parts.Length > 1 && parts[1].Length > 0)
                    Assert.Contains($"id=\"{parts[1]}\"", zip[file]);
            }
        }

        foreach (Match font in Font().Matches(zip["tangent.css"]))
            Assert.Contains(Resolved("tangent.css", font.Groups["file"].Value), zip.Keys);
    }

    /// <summary>A relative address as seen from the file <paramref name="from"/>.</summary>
    private static string Resolved(string from, string address)
    {
        var parts = from.Split('/')[..^1].ToList();
        foreach (var part in address.Split('/'))
        {
            if (part == "..") parts.RemoveAt(parts.Count - 1);
            else if (part != ".") parts.Add(part);
        }

        return string.Join('/', parts);
    }

    [GeneratedRegex("<(?:a|img|link)\\s[^>]*?(?:href|src)=\"(?<address>[^\"]*)\"")]
    private static partial Regex Target();

    [GeneratedRegex("url\\(['\"]?(?:\\./)?(?<file>fonts/[^'\")]+)")]
    private static partial Regex Font();
}
