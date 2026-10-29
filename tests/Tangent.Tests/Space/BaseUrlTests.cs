using System.Net;
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
            new Dictionary<string, string?> { ["Tangent:BaseUrl"] = "https://tangent.example/notes" },
            ct: Cancellation);

        Assert.Equal(HttpStatusCode.OK, (await tangentSpace.Client.GetAsync("/notes/health/live", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tangentSpace.Client.GetAsync("/notes/health/ready", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tangentSpace.Client.GetAsync("/health/live", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tangentSpace.Client.GetAsync("/health/ready", Cancellation)).StatusCode);
    }
}
