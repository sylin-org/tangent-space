using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Tangent.Render;

/// <summary>The page around a resource: the stylesheet and scripts it links answer as static files, and the
/// door's page carries the fragment island the page script mounts.</summary>
public sealed class ShellTests
{
    private const string BaseUrl = "https://tangent.example";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/tangent.css", "text/css")]
    [InlineData("/theme.js", "text/javascript")]
    [InlineData("/tangent.js", "text/javascript")]
    public async Task Each_linked_file_answers_from_the_web_root(string path, string mediaType)
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);

        using var response = await tangentSpace.Client.GetAsync(path, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Headers.ETag);
        Assert.NotNull(response.Content.Headers.LastModified);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(Cancellation));
    }

    [Theory]
    [InlineData("/tangent.css")]
    [InlineData("/theme.js")]
    [InlineData("/tangent.js")]
    public async Task Under_a_base_url_path_each_linked_file_answers_only_under_the_path(string path)
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Koan:Web:ApplicationUrl"] = "https://www.example.com/notes" }, ct: Cancellation);

        using var under = await tangentSpace.Client.GetAsync("/notes" + path, Cancellation);
        using var outside = await tangentSpace.Client.GetAsync(path, Cancellation);

        Assert.Equal(HttpStatusCode.OK, under.StatusCode);
        Assert.NotEmpty(await under.Content.ReadAsByteArrayAsync(Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
    }

    [Fact]
    public async Task The_door_links_the_files_and_renders_the_fragment_island()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Koan:Web:ApplicationUrl"] = BaseUrl }, ct: Cancellation);

        var html = await Read(tangentSpace, fragment: false);
        var fragment = await Read(tangentSpace, fragment: true);

        Assert.Contains($"<link rel=\"stylesheet\" href=\"{BaseUrl}/tangent.css\" />", html);
        Assert.Contains($"<script src=\"{BaseUrl}/theme.js\"></script>", html);
        Assert.Contains($"<script type=\"module\" src=\"{BaseUrl}/tangent.js\"></script>", html);
        var island = $"<main data-island=\"fragment\" data-props=\"{{&quot;url&quot;:&quot;{BaseUrl}/&quot;}}\">";
        Assert.Contains(island, html);
        Assert.StartsWith(island, fragment.Trim());
        Assert.EndsWith("</main>", fragment.Trim());
        Assert.DoesNotContain("<script", fragment);
    }

    private static async Task<string> Read(TangentSpaceFixture tangentSpace, bool fragment)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        if (fragment) request.Headers.Add(HtmlFormatter.FragmentHeader, "1");
        using var response = await tangentSpace.Client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Cancellation);
    }
}
