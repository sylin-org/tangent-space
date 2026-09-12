using System.Net.Http.Json;
using System.Security.Cryptography;
using Koan.Data.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TangentSpace.Participants;
using TangentSpace.Participation;
using Xunit;

namespace TangentSpace.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class ProfileCacheTests
{
    [Fact]
    public async Task Local_snapshot_survives_hot_cache_loss_and_updates_live_after_media_is_ready()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var profiles = app.Services.GetRequiredService<ParticipantProfiles>();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var initial = await profiles.Read(app.HumanParticipantId, budget.Token);
        Assert.Equal("loading", initial.Status);
        Assert.Null(initial.Avatar);
        var (credential, token) = ParticipantCredential.Issue(app.AgentParticipantId, "Profile live check", 1,
            [ParticipationGrants.Welcome], DateTimeOffset.UtcNow);
        using (EntityContext.NoCache()) await credential.Save();
        using var browser = new HttpClient { BaseAddress = new Uri(app.Origin) };
        browser.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var events = await browser.GetAsync("/api/profile-cache/events?did=" + Uri.EscapeDataString(ExperienceWebApp.HumanDid), HttpCompletionOption.ResponseHeadersRead, budget.Token);
        events.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await events.Content.ReadAsStreamAsync(budget.Token));

        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l1sAAAAASUVORK5CYII=");
        var filename = Convert.ToHexStringLower(SHA256.HashData(bytes)) + ".png";
        var media = ProfileCapture.MediaDirectory(app.Services.GetRequiredService<IWebHostEnvironment>());
        Directory.CreateDirectory(media);
        await File.WriteAllBytesAsync(Path.Combine(media, filename), bytes, budget.Token);
        await profiles.Store(new() { Id = ExperienceWebApp.HumanDid, DisplayName = "Leo", AvatarFile = filename,
            CapturedAt = DateTimeOffset.UtcNow, RetryAfter = DateTimeOffset.UtcNow.AddHours(6) }, true, budget.Token);
        string? line;
        do { line = await reader.ReadLineAsync(budget.Token); } while (line is not null && !line.StartsWith("data: "));
        Assert.NotNull(line);
        Assert.Contains("\"displayName\":\"Leo\"", line);
        using var hot = new MemoryCache(new MemoryCacheOptions());
        var restarted = new ParticipantProfiles(hot, app.Services.GetRequiredService<ParticipantDirectory>(), TimeProvider.System);
        var restored = await restarted.Read(app.HumanParticipantId, budget.Token);
        Assert.Equal("Leo", restored.DisplayName);
        Assert.StartsWith("/api/profile-cache/avatar?", restored.Avatar);
        using var avatar = await browser.GetAsync(restored.Avatar, budget.Token);
        Assert.Equal("image/png", avatar.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await avatar.Content.ReadAsByteArrayAsync(budget.Token));
        Assert.Contains("immutable", avatar.Headers.CacheControl!.ToString());
    }
}
