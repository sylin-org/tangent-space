using Koan.Web.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tangent.Space;

/// <summary>The first start: the data folder, no row until first run, and a configured base URL as Koan holds it.</summary>
public sealed class TangentSpaceTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_first_start_creates_the_data_folder_and_no_row()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);

        Assert.Equal(Path.Combine(tangentSpace.Folder, "data"), tangentSpace.DataFolder);
        Assert.True(Directory.Exists(tangentSpace.DataFolder));
        Assert.True(File.Exists(tangentSpace.DatabasePath));
        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task The_base_url_is_held_normalised()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { [TangentModule.BaseUrlKey] = "HTTPS://Tangent.Example:443/Field%20Notes/" },
            ct: Cancellation);

        Assert.Equal("https://tangent.example/Field%20Notes", tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>().Configured);
    }

    [Fact]
    public async Task A_base_url_that_is_not_a_url_fails_start_naming_the_key()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { [TangentModule.BaseUrlKey] = "tangent.example/notes" },
            ct: Cancellation));

        Assert.StartsWith($"{TangentModule.BaseUrlKey} must be the application's public URL", failure.Message);
    }
}
