using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tangent.Participants;

/// <summary>What participants need composed beside the data: the data-protection key ring in the data folder.</summary>
public sealed class CompositionTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_data_protection_key_ring_lives_in_the_data_folder()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);

        var protector = tangentSpace.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("composition");
        var keys = Path.Combine(tangentSpace.DataFolder, ParticipantsComposition.KeyFolder);

        Assert.Equal("kept", protector.Unprotect(protector.Protect("kept")));
        Assert.True(Directory.Exists(keys));
        Assert.Single(Directory.GetFiles(keys, "key-*.xml"));
    }
}
