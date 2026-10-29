using Koan.Web.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tangent.Participants;

/// <summary>What participants need composed beside the data: the data-protection key ring in the data folder, through
/// Koan's <c>Koan:Web:KeyRingDirectory</c>.</summary>
public sealed class CompositionTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_data_protection_key_ring_lives_in_the_data_folder()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);

        var keys = Path.Combine(tangentSpace.DataFolder, ParticipantsComposition.KeyFolder);
        var ring = tangentSpace.Services.GetRequiredService<KoanWebKeyRing>();
        var protector = tangentSpace.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("composition");

        // Koan prepares the folder at start and remembers the application's name in it.
        Assert.Equal(keys, ring.Directory);
        Assert.True(File.Exists(Path.Combine(keys, "koan-application-name.txt")));
        Assert.Equal("kept", protector.Unprotect(protector.Protect("kept")));
        Assert.Single(Directory.GetFiles(keys, "key-*.xml"));
    }
}
