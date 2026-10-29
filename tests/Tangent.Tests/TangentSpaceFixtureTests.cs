using Koan.Web.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Space;
using Xunit;

namespace Tangent;

/// <summary>The fixture's own promise: two Tangent Spaces in one process each read their own database,
/// in the test's flow and in the requests they serve.</summary>
public sealed class TangentSpaceFixtureTests
{
    private const string RowRoute = "/fixture/row";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_tangent_space_serves_its_own_row_while_another_runs()
    {
        var first = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Koan:Web:ApplicationUrl"] = "https://first.example" },
            services => services.AddSingleton<IKoanEndpointContributor, RowEndpoint>(),
            Cancellation);
        await using var second = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Koan:Web:ApplicationUrl"] = "https://second.example" },
            services => services.AddSingleton<IKoanEndpointContributor, RowEndpoint>(),
            Cancellation);

        try
        {
            Assert.Equal("https://first.example", await first.Client.GetStringAsync(RowRoute, Cancellation));
            Assert.Equal("https://second.example", await second.Client.GetStringAsync(RowRoute, Cancellation));
            Assert.Equal("https://second.example", (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))?.BaseUrl);
        }
        finally
        {
            await first.DisposeAsync();
        }

        Assert.Equal("https://second.example", await second.Client.GetStringAsync(RowRoute, Cancellation));
        Assert.Equal("https://second.example", (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))?.BaseUrl);
    }

    /// <summary>Answers with the base URL of the row the request reads through Koan's Entity statics.</summary>
    private sealed class RowEndpoint : IKoanEndpointContributor
    {
        public void Map(IEndpointRouteBuilder endpoints)
            => endpoints.MapGet(RowRoute, async (CancellationToken ct)
                => (await TangentSpace.Get(TangentSpace.SingleRow, ct))?.BaseUrl ?? "");
    }
}
