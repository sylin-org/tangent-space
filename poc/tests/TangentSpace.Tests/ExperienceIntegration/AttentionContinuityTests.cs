using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Activity;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Identity;
using Tangent.Spaces;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class AttentionContinuityTests
{
    private static Task Compile(ExperienceWebApp app, string topic, CancellationToken ct)
        => (Task)typeof(ExperienceDigest).Assembly.GetType("Tangent.Activity.AttentionCompiler")!
            .GetMethod("CompileTopic", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [topic, app.Services, ct])!;

    private static async Task SeedTopic(ExperienceWebApp app, string key, CancellationToken ct, string tangent = ExperienceWebApp.TangentKey)
        => await new Topic { Id = key, TangentKey = tangent, Title = "Synthetic " + key,
            CreatorParticipantId = app.OwnerParticipantId, CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow, PolicyRevision = 1, Access = AccessMap.TopicDefaults() }.Save(ct);

    private static async Task SeedPost(string topic, string id, string author, long sequence, CancellationToken ct)
        => await new Post { Id = id, TopicKey = topic, AuthorParticipantId = author, Sequence = sequence,
            AcceptedAt = DateTimeOffset.UtcNow, Content = new PostContent("Synthetic " + id, DateTimeOffset.UtcNow, null), Facets = [] }.Save(ct);

    private static async Task<JsonElement> Read(HttpClient http, string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }

    [Fact]
    public async Task Scoped_attention_remains_reachable_and_limited_coverage_is_explicit()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        using var fresh = EntityContext.NoCache();
        var hub = app.Services.GetRequiredService<TangentServer>();
        const string target = "zz-scoped-target";
        await SeedTopic(app, target, ct);
        await SeedPost(target, "m-scoped", app.HumanParticipantId, 1, ct);
        await new TopicConversation { Id = target, LastSequence = 1 }.Save(ct);
        await hub.Participants.SetWatch(app.AgentParticipantId, target, WatchMode.All, ct);
        await Compile(app, target, ct);
        for (var i = 0; i < 101; i++) await SeedTopic(app, "a-fill-" + i.ToString("D3"), ct);
        var scoped = await Read(app.Http, "/api/v1/experience/updates?scopeRef=" + Uri.EscapeDataString(app.Origin + "::proj::" + target), ct);
        Assert.Single(scoped.GetProperty("attention").GetProperty("items").EnumerateArray());
        Assert.False(scoped.GetProperty("attention").GetProperty("incomplete").GetBoolean());
        var global = (await Read(app.Http, "/api/v1/experience/updates", ct)).GetProperty("attention");
        Assert.True(global.GetProperty("incomplete").GetBoolean());
        Assert.True(global.GetProperty("waitingCount").GetProperty("atLeast").GetBoolean());
        Assert.False(global.GetProperty("waitingCount").TryGetProperty("value", out var waiting) && waiting.ValueKind != JsonValueKind.Null);
        Assert.False(global.GetProperty("more").GetBoolean());
        await hub.Tangents.Create(app.OwnerParticipantId, "other", "Other", null, null, null, null, ct, readAudience: "Public");
        await hub.Tangents.SetMembership(app.OwnerParticipantId, "other", app.AgentParticipantId, TangentRole.Reader, ct);
        await SeedTopic(app, "zz-other", ct, "other");
        await hub.Topics.SetReadAudience(app.OwnerParticipantId, "zz-other", TopicReadAudience.Public, true, ct);
        await SeedPost("zz-other", "m-other", app.HumanParticipantId, 1, ct);
        await new TopicConversation { Id = "zz-other", LastSequence = 1 }.Save(ct);
        await hub.Participants.SetWatch(app.AgentParticipantId, "zz-other", WatchMode.All, ct);
        await Compile(app, "zz-other", ct);
        var tangent = await Read(app.Http, "/api/v1/experience/updates?scopeRef=" + Uri.EscapeDataString(app.Origin + "::other"), ct);
        Assert.Single(tangent.GetProperty("attention").GetProperty("items").EnumerateArray());
        using (var wrongParent = await app.Http.GetAsync("/api/v1/experience/updates?scopeRef=" + Uri.EscapeDataString(app.Origin + "::proj::zz-other"), ct))
            Assert.False(wrongParent.IsSuccessStatusCode);
        for (var i = 1; i <= 100; i++) await SeedPost(target, "m-unread-" + i.ToString("D3"), app.HumanParticipantId, i + 1, ct);
        await new TopicConversation { Id = target, LastSequence = 101 }.Save(ct); await Compile(app, target, ct);
        var limited = (await Read(app.Http, "/api/v1/experience/updates?scopeRef=" + Uri.EscapeDataString(app.Origin + "::proj::" + target), ct)).GetProperty("attention");
        Assert.True(limited.GetProperty("incomplete").GetBoolean());
        Assert.False(limited.GetProperty("newActivityCount").TryGetProperty("value", out var count) && count.ValueKind != JsonValueKind.Null);
        await hub.Participants.SetWatch(app.AgentParticipantId, target, WatchMode.None, ct);
        var muted = (await Read(app.Http, "/api/v1/experience/updates?scopeRef=" + Uri.EscapeDataString(app.Origin + "::proj::" + target), ct)).GetProperty("attention");
        Assert.Empty(muted.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Notifications_filter_before_paging_bind_continuation_and_share_the_arrival_budget()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        using var fresh = EntityContext.NoCache();
        foreach (var did in new[] { ExperienceWebApp.OwnerDid, ExperienceWebApp.AgentDid, ExperienceWebApp.HumanDid })
            await new ParticipantProfileSnapshot { Id = did, DisplayName = "Synthetic participant", CapturedAt = DateTimeOffset.UtcNow, RetryAfter = DateTimeOffset.UtcNow.AddDays(1) }.Save(ct);
        var hub = app.Services.GetRequiredService<TangentServer>(); const string topic = "000-notifications";
        await SeedTopic(app, topic, ct);
        await SeedPost(topic, "m-a-human", app.HumanParticipantId, 1, ct);
        for (var i = 0; i < 25; i++) await SeedPost(topic, "m-z-own-" + i.ToString("D3"), app.AgentParticipantId, i + 2, ct);
        await new TopicConversation { Id = topic, LastSequence = 26 }.Save(ct);
        await hub.Participants.SetWatch(app.AgentParticipantId, topic, WatchMode.All, ct); await Compile(app, topic, ct);
        var first = await Read(app.Http, "/api/workspace/notifications", ct);
        Assert.Contains(first.GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetString() == "m-a-human");
        Assert.DoesNotContain(first.GetProperty("items").EnumerateArray(), row => row.GetProperty("kind").GetString() == "write_settlement");
        for (var i = 0; i < 26; i++) await SeedPost(topic, "m-b-human-" + i.ToString("D3"), app.HumanParticipantId, i + 27, ct);
        await new TopicConversation { Id = topic, LastSequence = 52 }.Save(ct); await Compile(app, topic, ct);
        first = await Read(app.Http, "/api/workspace/notifications", ct);
        Assert.Equal(25, first.GetProperty("items").GetArrayLength());
        var cursor = first.GetProperty("nextCursor").GetString(); Assert.NotNull(cursor);
        var path = "/api/workspace/notifications?cursor=" + Uri.EscapeDataString(cursor!);
        var second = await Read(app.Http, path, ct);
        Assert.Equal(2, second.GetProperty("items").GetArrayLength()); Assert.False(second.GetProperty("more").GetBoolean());
        Assert.Empty(first.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetString())
            .Intersect(second.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetString())));
        await hub.Participants.SetWatch(app.AgentParticipantId, topic, WatchMode.None, ct);
        using (var changed = await app.Http.GetAsync(path, ct)) Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        await new ParticipantPreferences { Id = app.AgentParticipantId, NotifyFollowing = false }.Save(ct);
        var disabled = await Read(app.Http, "/api/workspace/notifications", ct);
        Assert.Empty(disabled.GetProperty("items").EnumerateArray()); Assert.False(disabled.GetProperty("more").GetBoolean());
        using (var settingsChanged = await app.Http.GetAsync(path, ct)) Assert.Equal(HttpStatusCode.BadRequest, settingsChanged.StatusCode);
        await hub.Participants.SetWatch(app.OwnerParticipantId, topic, WatchMode.All, ct); await Compile(app, topic, ct);
        for (var i = 0; i < 12; i++) await new SpaceMembership { Id = "synthetic-arrival-" + i.ToString("D3"), RequestedAt = DateTimeOffset.UtcNow }.Save(ct);
        using var owner = new HttpClient { BaseAddress = new Uri(app.Origin) }; owner.DefaultRequestHeaders.Authorization = new("Bearer", app.OwnerToken);
        var ownerPage = await Read(owner, "/api/workspace/notifications", ct);
        Assert.InRange(ownerPage.GetProperty("items").GetArrayLength(), 1, 25);
        Assert.Equal(5, ownerPage.GetProperty("items").EnumerateArray().Count(row => row.GetProperty("kind").GetString() == "new_people"));
        var ownerNext = await Read(owner, "/api/workspace/notifications?cursor=" + Uri.EscapeDataString(ownerPage.GetProperty("nextCursor").GetString()!), ct);
        Assert.InRange(ownerNext.GetProperty("items").GetArrayLength(), 1, 25);
        Assert.Equal(5, ownerNext.GetProperty("items").EnumerateArray().Count(row => row.GetProperty("kind").GetString() == "new_people"));
    }
}
