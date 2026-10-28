using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Community;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class TangentAdmissionWorkflowTests
{
    [Fact]
    public async Task Public_Tangent_requests_can_be_reviewed_and_members_can_leave()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var hub = app.Services.GetRequiredService<TangentServer>();
        using var owner = new HttpClient { BaseAddress = new Uri(app.Origin) };
        owner.DefaultRequestHeaders.Authorization = new("Bearer", app.OwnerToken);
        await hub.Tangents.Create(app.OwnerParticipantId, "request-space", "A place to join", null, null, null, null, ct,
            admission: TangentAdmission.Approval, readAudience: "Public");
        const string endpoint = "/api/v1/tangents/request-space/joining";

        var state = await app.Http.GetFromJsonAsync<JsonElement>(endpoint, ct);
        Assert.True(state.GetProperty("canJoin").GetBoolean());
        Assert.Equal("visitor", state.GetProperty("status").GetString());
        (await app.Http.PostAsJsonAsync(endpoint, new { }, ct)).EnsureSuccessStatusCode();
        Assert.Equal("pending", (await app.Http.GetFromJsonAsync<JsonElement>(endpoint, ct)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Http.GetAsync(endpoint + "/requests", ct)).StatusCode);
        var pending = await owner.GetFromJsonAsync<JsonElement>(endpoint + "/requests", ct);
        Assert.Equal(app.AgentParticipantId, Assert.Single(pending.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        (await owner.PostAsJsonAsync(endpoint + "/requests/" + app.AgentParticipantId, new { accept = true }, ct)).EnsureSuccessStatusCode();
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>(endpoint + "/requests", ct)).GetProperty("items").EnumerateArray());
        Assert.Equal("member", (await app.Http.GetFromJsonAsync<JsonElement>(endpoint, ct)).GetProperty("status").GetString());
        using var leave = new HttpRequestMessage(HttpMethod.Delete, endpoint) { Content = JsonContent.Create(new { }) };
        (await app.Http.SendAsync(leave, ct)).EnsureSuccessStatusCode();
        Assert.Equal("visitor", (await app.Http.GetFromJsonAsync<JsonElement>(endpoint, ct)).GetProperty("status").GetString());
        (await app.Http.PostAsJsonAsync(endpoint, new { }, ct)).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync(endpoint + "/requests/" + app.AgentParticipantId, new { accept = false }, ct)).EnsureSuccessStatusCode();
        Assert.Equal("refused", (await app.Http.GetFromJsonAsync<JsonElement>(endpoint, ct)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Admitted_Tangent_and_article_require_actual_scoped_membership()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var hub = app.Services.GetRequiredService<TangentServer>();
        await hub.Tangents.Create(app.OwnerParticipantId, "members-place", "Members place", null, null, null, null, ct, readAudience: "Admitted");
        await hub.Tangents.CreateTopic(app.OwnerParticipantId, "members-place", "members-thought", "A private thought", null, ct);
        Assert.Null(await hub.Tangents.Describe(app.AgentParticipantId, "members-place", ct));
        Assert.False((await hub.Topics.Describe(app.AgentParticipantId, "members-thought", ct))?.CanRead ?? false);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Http.GetAsync("/api/v1/tangents/members-place", ct)).StatusCode);
        await hub.Participants.SetRole(app.OwnerParticipantId, "members-place", null, ExperienceWebApp.AgentDid, ParticipantRole.Member, ct);
        Assert.NotNull(await hub.Tangents.Describe(app.AgentParticipantId, "members-place", ct));
        Assert.True((await hub.Topics.Describe(app.AgentParticipantId, "members-thought", ct))!.CanRead);
        Assert.Null(await hub.Tangents.Describe(app.HumanParticipantId, "members-place", ct));

        await hub.Tangents.Create(app.OwnerParticipantId, "public-place", "Public place", null, null, null, null, ct, admission: TangentAdmission.Open, readAudience: "Public");
        await hub.Tangents.CreateTopic(app.OwnerParticipantId, "public-place", "admitted-article", "For members", null, ct, TopicReadAudience.Restricted);
        Assert.False((await hub.Topics.Describe(app.AgentParticipantId, "admitted-article", ct))?.CanRead ?? false);
        await hub.Participants.Join(app.AgentParticipantId, "public-place", ct);
        Assert.True((await hub.Topics.Describe(app.AgentParticipantId, "admitted-article", ct))!.CanRead);
        await hub.Participants.Leave(app.AgentParticipantId, "public-place", ct);
        Assert.False((await hub.Topics.Describe(app.AgentParticipantId, "admitted-article", ct))?.CanRead ?? false);
        var publish = await hub.Topics.SetReadAudience(app.OwnerParticipantId, "admitted-article", TopicReadAudience.Public, true, ct);
        Assert.True(publish.Accepted);
        Assert.True((await hub.Topics.Describe(app.AgentParticipantId, "admitted-article", ct))!.CanRead);
    }

    [Fact]
    public async Task Public_reading_survives_sign_in_without_joining_approval_Tangent()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var hub = app.Services.GetRequiredService<TangentServer>();
        await hub.Tangents.Create(app.OwnerParticipantId, "public-reading", "Public reading", null, null, null, null, ct,
            admission: TangentAdmission.Approval, readAudience: "Public");
        await hub.Tangents.CreateTopic(app.OwnerParticipantId, "public-reading", "public-article", "Open to the web", null, ct, TopicReadAudience.Public);
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        const string route = "/api/v1/tangents/public-reading/topics/public-article";
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(route, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Http.GetAsync(route, ct)).StatusCode);
        var policy = await hub.Topics.ReadPolicyAsync(app.AgentParticipantId, "public-article", ct);
        Assert.True(policy.CanRead);
        Assert.False(policy.CanWrite);
        foreach (var query in new[] { "tangent=public-reading", "tangent=public-reading&topic=public-article" })
        {
            var watch = await app.Http.GetFromJsonAsync<JsonElement>("/api/workspace/follow?" + query, ct);
            var scopeRef = watch.GetProperty("scopeRef").GetString();
            (await app.Http.PutAsJsonAsync("/api/v1/experience/watches", new { scopeRef, mode = "all" }, ct)).EnsureSuccessStatusCode();
            Assert.True((await app.Http.GetFromJsonAsync<JsonElement>("/api/workspace/follow?" + query, ct)).GetProperty("following").GetBoolean());
        }
        Assert.Null(await TangentMembership.Get(TangentMembership.Key("public-reading", app.AgentParticipantId), ct));
        await hub.Participants.Join(app.AgentParticipantId, "public-reading", ct);
        Assert.True((await hub.Topics.ReadPolicyAsync(app.AgentParticipantId, "public-article", ct)).CanRead);
    }
}
