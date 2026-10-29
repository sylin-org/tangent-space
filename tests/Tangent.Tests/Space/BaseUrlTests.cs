using System.Net;
using System.Net.Http.Headers;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Tangent.Space;

/// <summary>Where the Tangent Space answers: at its base URL, and under the base URL's path when it has one.</summary>
public sealed class BaseUrlTests
{
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
    public async Task A_restart_under_another_base_url_moves_the_row_the_links_and_the_island()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (await TangentSpaceFixture.StartAsync(Settings(dataFolder, "https://first.example"), ct: Cancellation))
                Assert.Equal("https://first.example", (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))?.BaseUrl);

            await using var moved = await TangentSpaceFixture.StartAsync(Settings(dataFolder, "https://second.example"), ct: Cancellation);
            var row = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            using var response = await moved.Client.SendAsync(request, Cancellation);
            var html = await response.Content.ReadAsStringAsync(Cancellation);

            Assert.NotNull(row);
            Assert.Equal("https://second.example", row.BaseUrl);
            Assert.Equal(2, row.Version);
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

    private static Dictionary<string, string?> Settings(string dataFolder, string baseUrl)
        => new() { ["Tangent:DataFolder"] = dataFolder, ["Koan:Web:ApplicationUrl"] = baseUrl };
}
