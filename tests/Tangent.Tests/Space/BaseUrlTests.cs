using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Koan.Web.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Participants;
using Tangent.Writer;
using Xunit;

namespace Tangent.Space;

/// <summary>Where the Tangent Space answers: at its base URL, and under the base URL's path when it has one; with none
/// configured, at the address it accepted and keeps on its row, which its setter's sign-in gives it, and until then at
/// each request's own origin.</summary>
public sealed class BaseUrlTests
{
    private const string Leo = "did:plc:leo4address";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Both_health_routes_answer()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);

        Assert.Equal(HttpStatusCode.OK, (await tangentSpace.Client.GetAsync("/health/live", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tangentSpace.Client.GetAsync("/health/ready", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task Under_a_base_url_path_the_routes_answer_only_under_the_path()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Koan:Web:ApplicationUrl"] = "https://tangent.example/notes" },
            ct: Cancellation);

        Assert.Equal(HttpStatusCode.OK, (await tangentSpace.Client.GetAsync("/notes/health/live", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tangentSpace.Client.GetAsync("/notes/health/ready", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tangentSpace.Client.GetAsync("/health/live", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tangentSpace.Client.GetAsync("/health/ready", Cancellation)).StatusCode);

        using var door = new HttpRequestMessage(HttpMethod.Get, "/notes/");
        door.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var answered = await tangentSpace.Client.SendAsync(door, Cancellation);
        Assert.Equal(HttpStatusCode.OK, answered.StatusCode);
        Assert.Equal("text/html", answered.Content.Headers.ContentType?.MediaType);
        Assert.Contains("https://tangent.example/notes/tangent.js", await answered.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, (await tangentSpace.Client.GetAsync("/", Cancellation)).StatusCode);
    }

    [Fact]
    public async Task A_restart_under_another_base_url_moves_the_links_and_the_island()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var first = await TangentSpaceFixture.StartAsync(Settings(dataFolder, "https://first.example"), ct: Cancellation))
                await Given.AnEstablishedTangentSpace(first, ct: Cancellation);

            await using var moved = await TangentSpaceFixture.StartAsync(Settings(dataFolder, "https://second.example"), ct: Cancellation);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            using var response = await moved.Client.SendAsync(request, Cancellation);
            var html = await response.Content.ReadAsStringAsync(Cancellation);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("<script type=\"module\" src=\"https://second.example/tangent.js\"></script>", html);
            Assert.Contains("data-props=\"{&quot;url&quot;:&quot;https://second.example/&quot;}\"", html);
            Assert.DoesNotContain("first.example", html);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
        }
    }

    [Fact]
    public async Task With_nothing_configured_or_accepted_every_link_derives_from_the_origin_a_request_arrives_on()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await Reached.APlace(tangentSpace, Leo, address: null, Cancellation);
        var loopback = tangentSpace.Client.BaseAddress!.GetLeftPart(UriPartial.Authority);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var response = await tangentSpace.Client.SendAsync(request, Cancellation);
        var here = await response.Content.ReadAsStringAsync(Cancellation);
        var proxied = await Reached.On(tangentSpace, "https://tangent.example.org", Cancellation, path: "/", accept: "text/html");
        using var descriptor = JsonDocument.Parse(await Reached.On(tangentSpace, "https://tangent.example.org", Cancellation, path: "/tangent.json"));

        Assert.Contains($"<script type=\"module\" src=\"{loopback}/tangent.js\"></script>", here);
        Assert.Contains("<script type=\"module\" src=\"https://tangent.example.org/tangent.js\"></script>", proxied);
        Assert.Contains("data-props=\"{&quot;url&quot;:&quot;https://tangent.example.org/&quot;}\"", proxied);
        Assert.Contains("&quot;events&quot;:&quot;https://tangent.example.org/events&quot;", proxied);
        Assert.DoesNotContain(loopback, proxied);
        Assert.Equal("https://tangent.example.org", descriptor.RootElement.GetProperty("address").GetString());
    }

    [Fact]
    public async Task An_accepted_address_is_kept_on_the_row_through_the_writer_once_and_read_back_before_the_first_request_after_a_restart()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var first = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation))
            {
                await Reached.APlace(first, Leo, address: null, Cancellation);
                var committed = new ConcurrentQueue<object>();
                first.Services.GetRequiredService<WriteLoop>().Committed += commit => committed.Enqueue(commit.Command);
                var baseUrl = first.Services.GetRequiredService<KoanWebBaseUrl>();

                await baseUrl.Accept("HTTPS://Kept.Example:443/", Cancellation);
                Assert.Equal("https://kept.example", baseUrl.Current);
                Assert.Equal("https://kept.example", (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Address);
                Assert.Equal(new KeepAddress("https://kept.example"), Assert.Single(committed));

                // Keeping the address the row holds writes nothing.
                await baseUrl.Accept("https://kept.example", Cancellation);
                Assert.Single(committed);
            }

            await using var restarted = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation);
            Assert.Equal("https://kept.example", restarted.Services.GetRequiredService<KoanWebBaseUrl>().Accepted);
            using var descriptor = JsonDocument.Parse(await restarted.Client.GetStringAsync("/tangent.json", Cancellation));
            Assert.Equal("https://kept.example", descriptor.RootElement.GetProperty("address").GetString());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
        }
    }

    [Fact]
    public async Task Setting_the_place_up_at_sign_in_takes_the_origin_the_sign_in_completed_on_as_its_address()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation, environment: "Development");
        var origin = tangentSpace.Client.BaseAddress!.GetLeftPart(UriPartial.Authority);
        using var browser = new Browser(tangentSpace);

        using var landed = await browser.SignIn("leo", Leo, Cancellation, handle: "leo.sylin.org", comeBack: FirstRunController.Path);
        var pointers = WebUtility.HtmlDecode(await landed.Content.ReadAsStringAsync(Cancellation));

        Assert.Equal((HttpStatusCode.OK, FirstRunController.Path), (landed.StatusCode, landed.RequestMessage!.RequestUri!.AbsolutePath));
        Assert.Equal(origin, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Address);
        Assert.Equal(origin, tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>().Accepted);
        Assert.Contains($"<link rel=\"tangent\" href=\"{origin}\">", pointers);
    }

    [Fact(Skip = "Holds once Tangent's pin carries Koan's K11, where a stored address the host cannot use is not held; at Koan 2d7ddb15e this Production start is refused, naming the store.")]
    public async Task A_place_that_kept_an_http_loopback_address_in_development_starts_in_production_and_offers_its_public_origin()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var local = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation,
                environment: "Development"))
            {
                await Reached.APlace(local, Leo, address: null, Cancellation);
                await local.Services.GetRequiredService<KoanWebBaseUrl>().Accept("http://127.0.0.1:5220", Cancellation);
                Assert.Equal("http://127.0.0.1:5220", (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Address);
            }

            await using (var moved = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation))
            {
                Assert.Null(moved.Services.GetRequiredService<KoanWebBaseUrl>().Current);
                await Reached.On(moved, "https://tangent.example.org", Cancellation);
                await Reached.Settled(moved, Cancellation);
                Assert.Equal("https://tangent.example.org", Assert.Single(await Offers.Offer.All(Cancellation)).Subject);
            }

            await using var configured = await TangentSpaceFixture.StartAsync(new Dictionary<string, string?>
            {
                ["Tangent:DataFolder"] = dataFolder,
                [TangentModule.BaseUrlKey] = "https://configured.example"
            }, ct: Cancellation);
            Assert.Equal("https://configured.example", configured.Services.GetRequiredService<KoanWebBaseUrl>().Current);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
        }
    }

    private static Dictionary<string, string?> Settings(string dataFolder, string baseUrl)
        => new() { ["Tangent:DataFolder"] = dataFolder, ["Koan:Web:ApplicationUrl"] = baseUrl };
}
