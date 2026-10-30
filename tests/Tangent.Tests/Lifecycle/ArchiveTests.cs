using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
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

/// <summary>The archive and retirement: every address a visitor reads live answers from the folder, what they may not read
/// is nowhere in it, nothing in it needs a server, and a server named to it serves its pages and takes no write.</summary>
public sealed partial class ArchiveTests
{
    private const string Ida = "did:plc:ida4archive";
    private const string IdaHandle = "ida.example";
    private const string RetiredLine = "This place is retired.";

    [Fact]
    public async Task Every_address_a_visitor_reads_live_answers_from_the_folder_across_a_place_larger_than_one_read_page()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Someone(tangentSpace, Ida, IdaHandle);
        using var visitor = Agent(tangentSpace);

        // More Tangents than a page lists, more Topics than a page of a Tangent lists, more replies than a window holds,
        // and more of one participant's Posts than a page of their profile holds.
        foreach (var number in Enumerable.Range(1, TangentsController.PageSize))
            await Given.ATangent(tangentSpace, "more-" + number, ct: Cancellation);
        foreach (var number in Enumerable.Range(1, Math.Max(TopicList.PageSize, RecentPosts.PageSize)))
            await Started(writer, Ida, "Topic number " + number, "Words of topic " + number);
        var long_ = await Started(writer, Ida, "A long one", $"It goes on, says [@ida](at://{Ida}).");
        var replies = new List<Post>();
        foreach (var number in Enumerable.Range(1, Export.WindowSize + 1))
            replies.Add(await Replied(writer, long_, Ida, "Reply number " + number));
        var gone = await Replied(writer, long_, Ida, "Words nobody keeps.");
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(gone.Id, Ida), Cancellation));
        await Started(writer, Ida, "A piece", "An article's words.", Articles);

        var folder = Path.Combine(tangentSpace.Folder, "archive");
        Assert.Equal(BaseUrl, await Archive.Write(folder, tangentSpace.Services, Cancellation));

        // Everything a visitor is led to live, page after page, is at the same address in the folder.
        var addresses = new HashSet<string>(StringComparer.Ordinal) { BaseUrl + "/", BaseUrl + "/t", BaseUrl + "/p/" + IdaHandle };
        var door = (await Send(visitor, HttpMethod.Get, "/", json: true)).Json;
        foreach (var tangent in door.GetProperty("tangents").EnumerateArray()) addresses.Add(tangent.GetProperty("address").GetString()!);
        var listing = (await Send(visitor, HttpMethod.Get, "/t", json: true)).Json;
        while (true)
        {
            foreach (var tangent in listing.GetProperty("tangents").EnumerateArray())
                addresses.Add(tangent.GetProperty("address").GetString()!);
            if (!listing.TryGetProperty("next", out var next) || next.ValueKind != JsonValueKind.String) break;
            listing = (await Send(visitor, HttpMethod.Get, next.GetString()![BaseUrl.Length..], json: true)).Json;
        }

        var topics = 0;
        var page = (await Send(visitor, HttpMethod.Get, "/t/" + Workshop, json: true)).Json;
        while (true)
        {
            foreach (var topic in page.GetProperty("topics").EnumerateArray())
            {
                addresses.Add(topic.GetProperty("address").GetString()!);
                topics++;
            }

            if (!page.TryGetProperty("next", out var next) || next.ValueKind != JsonValueKind.String) break;
            page = (await Send(visitor, HttpMethod.Get, next.GetString()![BaseUrl.Length..], json: true)).Json;
        }

        Assert.True(topics > TopicList.PageSize);
        foreach (var address in addresses)
            Assert.True(File.Exists(Page(folder, address)), $"{address} answers live, and the folder holds no page there.");

        // A Tangent's and a Topic's continuations are in it, and the Tangents' list leads on.
        Assert.True(File.Exists(Path.Combine(folder, "t", "page-2.html")));
        Assert.True(File.Exists(Path.Combine(folder, "t", Workshop, "page-2.html")));
        Assert.Contains("href=\"page-2.html\"", await Read(folder, "t", "index.html"));
        Assert.Contains("href=\"2.html\"", await Read(folder, "t", Workshop, "a-long-one", "index.html"));
        Assert.Contains("href=\"page-2.html\"", await Read(folder, "p", IdaHandle, "index.html"));

        // A Post's address leads to the window that holds it, a Topic's root and a reply's alike.
        var last = await Read(folder, "t", Workshop, "a-long-one", replies[^1].Id, "index.html");
        Assert.Contains($"url=../2.html#{replies[^1].Id}", last);
        Assert.Contains($"id=\"{replies[^1].Id}\"", await Read(folder, "t", Workshop, "a-long-one", "2.html"));
        Assert.Contains($"url=../index.html#{long_.Id}", await Read(folder, "t", Workshop, "a-long-one", long_.Id, "index.html"));

        // The door is the door, and a removed Post stays a tombstone.
        Assert.Contains("id=\"door-title\"", await Read(folder, "index.html"));
        Assert.Contains("post-removed", await Read(folder, "t", Workshop, "a-long-one", "2.html"));
        Assert.DoesNotContain(Files(folder), file => File.ReadAllText(file).Contains("Words nobody keeps"));

        await AssertStandsAlone(folder);
    }

    [Fact]
    public async Task It_holds_what_a_visitor_may_read_with_the_feeds_as_they_answer_and_nothing_else()
    {
        const string members = "the-members";
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Someone(tangentSpace, Ida, IdaHandle);
        await Someone(tangentSpace, "did:plc:asking4archive", "asking.example", Standing.Applied);
        await Given.ATangent(tangentSpace, members, Audience.Private, ct: Cancellation);
        await Overwrites.SetAccess(tangentSpace.Services.GetRequiredService<RoleCollection>(), members, Roles.Member, RoleAccess.Post, Cancellation);
        await Started(writer, Ida, "For the members", "Words the members read", members);
        await Given.AParticipant(tangentSpace, "did:plc:leo4archive", [Roles.Administrator], ct: Cancellation);
        await Started(writer, "did:plc:leo4archive", "In the studio", "Words only the studio reads", Studio);
        var open = await Started(writer, Ida, "In the open", "Words anyone reads");
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(Tids.Mint(), members, "Taken in", "", Ida, OriginId: open.Id), Cancellation));
        await Started(writer, Ida, "A piece", "An article's words.", Articles);
        using var visitor = Agent(tangentSpace);

        var folder = Path.Combine(tangentSpace.Folder, "archive");
        await Archive.Write(folder, tangentSpace.Services, Cancellation);

        Assert.True(File.Exists(Path.Combine(folder, "t", Workshop, "in-the-open", "index.html")));
        foreach (var absent in new[] { members, Studio }) Assert.False(Directory.Exists(Path.Combine(folder, "t", absent)));
        Assert.False(Directory.Exists(Path.Combine(folder, "p", "asking.example")));
        Assert.DoesNotContain(Files(folder), file => File.ReadAllText(file) is var text
            && (text.Contains("Words the members read") || text.Contains("only the studio reads") || text.Contains("Taken in") || text.Contains(members)));

        // Each feed is the file its address answers live, and the pages name them.
        foreach (var path in new[] { $"/t/{Workshop}/feed.atom", $"/t/{Workshop}/feed.json", $"/t/{Articles}/feed.atom", "/articles.atom", "/articles.json" })
            Assert.Equal((await Send(visitor, HttpMethod.Get, path)).Body, await File.ReadAllTextAsync(folder + path, Cancellation));
        Assert.False(File.Exists(Path.Combine(folder, "t", members, "feed.atom")));
        Assert.Contains($"href=\"{BaseUrl}/t/{Workshop}/feed.atom\"", await Read(folder, "t", Workshop, "index.html"));
        Assert.Contains($"href=\"{BaseUrl}/t/{Workshop}/feed.atom\"", await Read(folder, "t", Workshop, "in-the-open", "index.html"));
        Assert.Contains($"href=\"{BaseUrl}/articles.atom\"", await Read(folder, "index.html"));

        await AssertStandsAlone(folder);
    }

    [Fact]
    public async Task A_private_place_or_a_folder_in_use_is_refused_and_leaves_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, "Behind a door", Audience.Private, ct: Cancellation);
        var folder = Path.Combine(tangentSpace.Folder, "archive");

        var refused = await Assert.ThrowsAsync<LifecycleRefusal>(() => Archive.Write(folder, tangentSpace.Services, Cancellation));

        Assert.Contains("A private place has nothing to archive", refused.Message);
        Assert.Empty(Directory.GetDirectories(tangentSpace.Folder, "archive*"));

        await using var open = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Given.AnEstablishedTangentSpace(open, "In the open", ct: Cancellation);
        var taken = Path.Combine(open.Folder, "taken");
        Directory.CreateDirectory(taken);
        await File.WriteAllTextAsync(Path.Combine(taken, "kept.txt"), "kept", Cancellation);

        var occupied = await Assert.ThrowsAsync<LifecycleRefusal>(() => Archive.Write(taken, open.Services, Cancellation));

        Assert.Contains("not empty", occupied.Message);
        Assert.Equal(["kept.txt"], Directory.GetFiles(taken).Select(file => Path.GetFileName(file)!));
        Assert.False(Directory.Exists(taken + ".partial"));
    }

    [Fact]
    public async Task A_retired_server_serves_the_pages_at_their_addresses_and_refuses_every_write()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Someone(tangentSpace, Ida, IdaHandle);
        var root = await Started(writer, Ida, "Still here", "Words that stay.");
        var folder = Path.Combine(tangentSpace.Folder, "archive");
        await Archive.Write(folder, tangentSpace.Services, Cancellation);
        await tangentSpace.StopAsync(Cancellation);

        await using var retired = await Retired(folder);
        using var client = Client(retired);

        // A page at its address, whose relative links hold once its closing slash is added, as a static host adds it.
        var bare = await Send(client, HttpMethod.Get, TopicPath(root));
        Assert.Equal(HttpStatusCode.MovedPermanently, bare.Status);
        Assert.Equal(TopicPath(root) + "/", new Uri(bare.Location!, UriKind.RelativeOrAbsolute).IsAbsoluteUri ? new Uri(bare.Location!).AbsolutePath : bare.Location);
        var page = await Send(client, HttpMethod.Get, TopicPath(root) + "/");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Words that stay.", page.Body);
        Assert.Contains(RetiredLine, page.Body);
        Assert.Contains(RetiredLine, (await Send(client, HttpMethod.Get, "/")).Body);
        var feed = await Send(client, HttpMethod.Get, $"/t/{Workshop}/feed.atom");
        Assert.Equal((HttpStatusCode.OK, "application/atom+xml"), (feed.Status, feed.MediaType));
        Assert.Equal(HttpStatusCode.OK, (await Send(client, HttpMethod.Get, Retirement.Health)).Status);

        // No write lands anywhere, and what the archive does not hold is not found.
        foreach (var address in new[] { "/", TopicPath(root), TopicPath(root) + "/", "/first-run" })
        {
            var write = await Send(client, HttpMethod.Post, address, new FormUrlEncodedContent([new("body", "One more word")]));
            Assert.Equal((HttpStatusCode.MethodNotAllowed, "application/problem+json"), (write.Status, write.MediaType));
            Assert.Contains("retired", write.Title);
        }

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Send(client, HttpMethod.Put, TopicPath(root) + "/")).Status);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await Send(client, HttpMethod.Delete, TopicPath(root) + "/")).Status);
        var signIn = await Send(client, HttpMethod.Get, "/sign-in");
        Assert.Equal(HttpStatusCode.NotFound, signIn.Status);
        Assert.Contains("retired", signIn.Body);

        // A place under a path is served under it.
        await using var under = await Retired(folder, "https://tangent.example/notes");
        using var notes = Client(under);
        Assert.Contains("Words that stay.", (await Send(notes, HttpMethod.Get, "/notes" + TopicPath(root) + "/")).Body);
    }

    [Fact]
    public void No_archive_named_is_no_retirement_and_one_that_is_not_there_is_no_start()
    {
        var unnamed = WebApplication.CreateBuilder();
        Assert.Null(Retirement.Compose(unnamed));

        var empty = Path.Combine(Path.GetTempPath(), "tangent-no-archive-" + Guid.CreateVersion7().ToString("n"));
        var named = WebApplication.CreateBuilder();
        named.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Tangent:Archive"] = empty });
        Assert.Contains("holds no archive", Assert.Throws<InvalidOperationException>(() => Retirement.Compose(named)).Message);
    }

    /// <summary>A participant whose handle names a folder on any machine, which an id's colons do not.</summary>
    private static async Task Someone(TangentSpaceFixture tangentSpace, string id, string handle, Standing standing = Standing.Admitted)
    {
        var participant = new Participant { Id = id, Handle = handle, Standing = standing };
        Assert.IsType<Outcome<Participant>.Done>(await tangentSpace.Services.GetRequiredService<WriteLoop>().Send(new Store<Participant>(participant), Cancellation));
        if (standing == Standing.Admitted) await tangentSpace.Services.GetRequiredService<RoleCollection>().Add(Roles.Member, id, Cancellation);
    }

    private static async Task<WebApplication> Retired(string folder, string? baseUrl = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = Path.GetDirectoryName(folder), EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Tangent:Archive"] = folder, [TangentModule.BaseUrlKey] = baseUrl });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var application = Assert.IsType<WebApplication>(Retirement.Compose(builder));
        await application.StartAsync(Cancellation);
        return application;
    }

    private static HttpClient Client(WebApplication application) => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        BaseAddress = new Uri(application.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single())
    };

    /// <summary>The file a static host answers an address of the place with.</summary>
    private static string Page(string folder, string address)
        => Path.Combine(folder, Uri.UnescapeDataString(address[BaseUrl.Length..]).Trim('/'), "index.html");

    private static Task<string> Read(string folder, params string[] path)
        => File.ReadAllTextAsync(Path.Combine([folder, .. path]), Cancellation);

    private static string[] Files(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
        .Where(file => !file.Contains(Path.DirectorySeparatorChar + HtmlFonts + Path.DirectorySeparatorChar)).ToArray();

    private const string HtmlFonts = Render.HtmlFormatter.Fonts;

    /// <summary>Every page says the place is retired and offers nothing that needs a server: no script, no form, no island
    /// and no sign-in; and each link and image lands on a file the folder holds, at a place that file holds, whether it
    /// goes there from the page or by the place's own address.</summary>
    private static async Task AssertStandsAlone(string folder)
    {
        Assert.All(Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories), file => Assert.Contains(Path.GetFileName(file), new[] { "feed.json", "articles.json" }));
        foreach (var file in Files(folder).Where(file => file.EndsWith(".html", StringComparison.Ordinal)))
        {
            var content = await File.ReadAllTextAsync(file, Cancellation);
            Assert.Contains(RetiredLine, content);
            foreach (var forbidden in new[] { "<script", "<form", "data-island", "/sign-in", ">Sign in<" }) Assert.DoesNotContain(forbidden, content);
            foreach (Match target in Target().Matches(content))
            {
                var address = WebUtility.HtmlDecode(target.Groups["address"].Value);
                if (address.StartsWith("https://", StringComparison.Ordinal) && !address.StartsWith(BaseUrl, StringComparison.Ordinal)) continue;
                var parts = address.Split('#');
                var path = parts[0].Length == 0 ? file
                    : parts[0].StartsWith(BaseUrl, StringComparison.Ordinal) ? Path.GetFullPath(folder + Uri.UnescapeDataString(parts[0][BaseUrl.Length..]))
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, Uri.UnescapeDataString(parts[0])));
                if (Directory.Exists(path)) path = Path.Combine(path, "index.html");
                Assert.True(File.Exists(path), $"{file} links {address}, and the folder holds no {path}.");
                if (parts.Length > 1 && parts[1].Length > 0) Assert.Contains($"id=\"{parts[1]}\"", await File.ReadAllTextAsync(path, Cancellation));
            }
        }
    }

    [GeneratedRegex("<(?:a|img|link)\\s[^>]*?(?:href|src)=\"(?<address>[^\"]*)\"")]
    private static partial Regex Target();
}
