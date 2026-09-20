using System.Diagnostics;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Identity;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

/// <summary>A timing canary for the profile read: seeds a realistic world (one author,
/// many posts) and reports how long the profile envelope and the roles read take.
/// Timings land in profile-timings.txt; the assertion is only a sanity bound.</summary>
[Collection("Experience integration")]
public sealed class ProfileTimingTests
{
    [Fact]
    public async Task Profile_read_timings_with_a_realistic_history()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var now = DateTimeOffset.UtcNow;

        // Three hundred posts by the human across the seeded topic: a heavy history.
        for (var index = 0; index < 300; index++)
        {
            await ExperienceWebApp.Accept(ExperienceWebApp.TopicKey, app.HumanParticipantId,
                $"timing-{index}", 10 + index, now.AddSeconds(-index), $"Timing post {index}", replyTo: null);
        }

        var (humanCredential, humanToken) = ParticipantCredential.Issue(app.HumanParticipantId, "Profile timing", 1,
            [ParticipationGrants.Welcome], DateTimeOffset.UtcNow.AddHours(1));
        using (EntityContext.NoCache()) await humanCredential.Save();
        using var human = new HttpClient { BaseAddress = new Uri(app.Origin) };
        human.DefaultRequestHeaders.Authorization = new("Bearer", humanToken);

        var lines = new List<string>();
        var cold = await TimeAsync(() => human.GetAsync($"/api/participants/{app.HumanParticipantId}/profile"));
        lines.Add($"profile cold: {cold.TotalMilliseconds:F0} ms");
        for (var run = 1; run <= 3; run++)
        {
            var warm = await TimeAsync(() => human.GetAsync($"/api/participants/{app.HumanParticipantId}/profile"));
            lines.Add($"profile warm #{run}: {warm.TotalMilliseconds:F0} ms");
        }

        var roles = await TimeAsync(() => human.GetAsync($"/api/roles/ui/participant/{app.HumanParticipantId}/roles"));
        lines.Add($"roles read: {roles.TotalMilliseconds:F0} ms");

        await File.WriteAllLinesAsync(Path.Combine(Path.GetTempPath(), "profile-timings.txt"), lines);
        Assert.True(cold.TotalSeconds < 30, $"profile cold read took {cold.TotalMilliseconds:F0} ms");
    }

    private static async Task<TimeSpan> TimeAsync(Func<Task<HttpResponseMessage>> request)
    {
        var clock = Stopwatch.StartNew();
        using var response = await request();
        clock.Stop();
        return clock.Elapsed;
    }
}
