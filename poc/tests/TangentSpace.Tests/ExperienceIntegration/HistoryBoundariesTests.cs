using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Api;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Identity;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class HistoryBoundariesTests
{
    [Fact]
    public async Task History_uses_current_classification_and_effective_management_and_scopes_each_snapshot()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var topics = app.Services.GetRequiredService<TopicGovernance>();
        var agent = (await Participant.Get(app.AgentParticipantId, ct))!;
        agent.Declare(ParticipantClassification.Agent);
        await agent.Save(ct);
        Assert.True((await topics.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            app.AgentParticipantId, TopicRole.Manager, ct)).Accepted);
        Assert.True((await topics.SetSettings(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            true, null, null, null, ct)).Accepted);
        await app.Services.GetRequiredService<ConversationService>().ChangePost(app.HumanParticipantId,
            ExperienceWebApp.TopicKey, "m-leo-r1", "A revised summary", false, ct);
        var live = (await Post.Get("m-leo-r1", ct))!;
        var path = "/api/history/messages?set=changelog&postId=m-leo-r1";
        using (var allowed = await app.Http.GetAsync(path, ct))
        {
            allowed.EnsureSuccessStatusCode();
            using var rows = JsonDocument.Parse(await allowed.Content.ReadAsStringAsync(ct));
            Assert.Contains(rows.RootElement.EnumerateArray(), row => row.GetProperty("id").GetString() == live.ChangeId);
        }
        using (var mismatch = await app.Http.GetAsync($"/api/history/messages/{live.ChangeId}?set=changelog&postId=m-agent-q1", ct))
            Assert.Equal(HttpStatusCode.NotFound, mismatch.StatusCode);

        var tangent = (await Community.Tangent.Get(ExperienceWebApp.TangentKey, ct))!;
        tangent.ChangeParticipationPolicy(app.OwnerParticipantId, TangentAdmission.Open,
            ParticipationPreset.HumansOnly, UndeclaredAccess.Deny, DateTimeOffset.UtcNow);
        await tangent.Save(ct);
        Assert.False((await topics.ReadPolicyAsync(app.AgentParticipantId, ExperienceWebApp.TopicKey, ct)).CanManage);
        using (var refused = await app.Http.GetAsync(path, ct))
            Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);

        tangent.ChangeParticipationPolicy(app.OwnerParticipantId, TangentAdmission.Open,
            ParticipationPreset.Everyone, UndeclaredAccess.Write, DateTimeOffset.UtcNow);
        await tangent.Save(ct);
        Assert.True((await topics.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            app.AgentParticipantId, TopicRole.Member, ct)).Accepted);
        var roles = app.Services.GetRequiredService<RoleCollection>();
        await roles.Define("role:history-review", "History review", [TangentPermissions.ManageTopics], ct: ct);
        await roles.Add("role:history-review", app.AgentParticipantId, ct);
        using var roleAllowed = await app.Http.GetAsync(path, ct);
        roleAllowed.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Both_parent_and_topic_access_require_confirmation_before_publishing_history()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);
        var topics = app.Services.GetRequiredService<TopicGovernance>();
        Assert.True((await topics.SetReadAudience(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            TopicReadAudience.Public, false, ct)).Accepted); // Still effectively restricted by its parent.
        var parentPath = $"/api/v1/tangents/{ExperienceWebApp.TangentKey}/access";
        var topicPath = $"/api/v1/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}/access";
        var reader = app.Services.GetRequiredService<PublicConversationReader>();
        using (var invalid = await app.Http.PutAsJsonAsync(parentPath,
            new { access = new AccessMap { See = ["not-a-role"] }, publishExistingHistory = true }, ct))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Null(await reader.ReadPost(ExperienceWebApp.TangentKey, "m-leo-r1", 5, ct));
        using (var denied = await app.Http.PutAsJsonAsync(parentPath,
            new { access = new AccessMap { See = [AccessTokens.Everyone] }, publishExistingHistory = false }, ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using var problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync(ct));
            Assert.Equal("history_publication_required", problem.RootElement.GetProperty("code").GetString());
        }
        Assert.Null(await reader.ReadPost(ExperienceWebApp.TangentKey, "m-leo-r1", 5, ct));
        using (var accepted = await app.Http.PutAsJsonAsync(parentPath,
            new { access = new AccessMap { See = [AccessTokens.Everyone] }, publishExistingHistory = true }, ct))
            accepted.EnsureSuccessStatusCode();
        Assert.NotNull(await reader.ReadPost(ExperienceWebApp.TangentKey, "m-leo-r1", 5, ct));
        using (var narrow = await app.Http.PutAsJsonAsync(topicPath,
            new { access = new AccessMap { See = [AccessTokens.Authenticated] }, publishExistingHistory = false }, ct))
            narrow.EnsureSuccessStatusCode();
        using (var denied = await app.Http.PutAsJsonAsync(topicPath,
            new { access = new AccessMap(), publishExistingHistory = false }, ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using var problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync(ct));
            Assert.Equal("history_publication_required", problem.RootElement.GetProperty("code").GetString());
        }
        Assert.Null(await reader.ReadPost(ExperienceWebApp.TangentKey, "m-leo-r1", 5, ct));
    }

    [Fact]
    public async Task Public_anchor_survives_large_escaped_neighbors_without_holes_or_oversized_response()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        await app.Services.GetRequiredService<TangentGovernance>().SetAccess(app.OwnerParticipantId,
            ExperienceWebApp.TangentKey, new AccessMap(), ct);
        Assert.True((await app.Services.GetRequiredService<TopicGovernance>().SetReadAudience(app.OwnerParticipantId,
            ExperienceWebApp.TopicKey, TopicReadAudience.Public, true, ct)).Accepted);
        for (var n = 4; n <= 10; n++)
            await ExperienceWebApp.Accept(ExperienceWebApp.TopicKey, app.HumanParticipantId, $"large-{n}", n,
                DateTimeOffset.UtcNow, new string('<', 4096), null);
        var document = await app.Services.GetRequiredService<PublicConversationReader>()
            .ReadPost(ExperienceWebApp.TangentKey, "m-large-7", 7, ct);
        Assert.NotNull(document);
        Assert.Contains(document.Window.Posts, post => post.Id == "m-large-7");
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(document.Window).Length <= 1024 * 1024);
        var sequences = document.Window.Posts.Select(post => int.Parse(post.Id["m-large-".Length..])).ToArray();
        Assert.Equal(Enumerable.Range(sequences[0], sequences.Length), sequences);
    }
}

