using Microsoft.Extensions.Options;
using Xunit;

namespace Tangent.Space;

/// <summary>The first start: the data folder, the one settings row, and the base URL it records.</summary>
public sealed class TangentSpaceTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_first_start_creates_the_data_folder_and_exactly_one_row()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);

        Assert.Equal(Path.Combine(tangentSpace.Folder, "data"), tangentSpace.DataFolder);
        Assert.True(Directory.Exists(tangentSpace.DataFolder));
        Assert.True(File.Exists(tangentSpace.DatabasePath));
        Assert.Equal(1, await TangentSpace.Count.Exact(Cancellation));
        Assert.NotNull(await TangentSpace.Get(TangentSpace.SingleRow, Cancellation));
    }

    [Fact]
    public async Task The_base_url_is_persisted_normalised()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Tangent:BaseUrl"] = "HTTPS://Tangent.Example:443/Field%20Notes/" },
            ct: Cancellation);

        var row = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);

        Assert.NotNull(row);
        Assert.Equal("https://tangent.example/Field Notes", row.BaseUrl);
        Assert.Equal(row.BaseUrl, tangentSpace.Options.BaseUrl);
    }

    [Fact]
    public async Task An_invalid_base_url_fails_start_with_the_rule_message()
    {
        var failure = await Assert.ThrowsAsync<AggregateException>(() => TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Tangent:BaseUrl"] = "tangent.example/notes" },
            ct: Cancellation));

        Assert.NotEmpty(failure.InnerExceptions);
        Assert.All(failure.InnerExceptions, inner => Assert.Contains(
            "Tangent:BaseUrl must be an absolute http or https URL: scheme, host and an optional path.",
            Assert.IsType<OptionsValidationException>(inner).Failures));
    }
}
