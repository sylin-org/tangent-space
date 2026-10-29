using Microsoft.Extensions.DependencyInjection;
using Tangent.Writer;
using Xunit;

namespace Tangent.Tangents;

/// <summary>An overwrite row keyed by Tangent and subject.</summary>
public sealed class OverwriteTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_overwrite_round_trips_through_its_tangent_and_subject_with_its_allow_and_deny()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var overwrite = new Overwrite
        {
            Id = Overwrite.Key("the-workshop", "role:moderators"),
            TangentId = "the-workshop",
            SubjectId = "role:moderators",
            Allow = ["Moderate", "Topic"],
            Deny = ["ManageTangent"]
        };

        Assert.IsType<Outcome<Overwrite>.Done>(await writer.Send(new Store<Overwrite>(overwrite), Cancellation));
        var stored = await Overwrite.Get(Overwrite.Key("the-workshop", "role:moderators"), Cancellation);

        Assert.NotNull(stored);
        Assert.Equal(("the-workshop", "role:moderators"), (stored.TangentId, stored.SubjectId));
        Assert.Equal(["Moderate", "Topic"], stored.Allow);
        Assert.Equal(["ManageTangent"], stored.Deny);
    }
}
