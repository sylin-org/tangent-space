using System.Net.Http.Json;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Activity;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Identity;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class PublicFollowNotificationsTests
{
    [Fact]
    public async Task Public_nonmembers_receive_followed_topic_and_Tangent_notifications_under_current_policy()
    {
        await using var app = await ExperienceWebApp.StartAsync(jobMode: Koan.Jobs.JobMode.Normal);
        var ct = TestContext.Current.CancellationToken;
        var hub = app.Services.GetRequiredService<TangentServer>();
        await hub.Tangents.Create(app.OwnerParticipantId, "public-follow", "Public follow", null, null, null, null, ct,
            admission: TangentAdmission.Approval, readAudience: "Public");
        await hub.Tangents.CreateTopic(app.OwnerParticipantId, "public-follow", "follow-thought", "A thought to follow", null, ct, TopicReadAudience.Public);
        await hub.Participants.SetWatch(app.AgentParticipantId, "follow-thought", WatchMode.All, ct);
        await hub.Participants.SetTangentWatch(app.HumanParticipantId, "public-follow", WatchMode.All, ct);
        var post = await hub.Posts.CreatePost(app.OwnerParticipantId, "follow-thought", new PostCreateRequest("followed-reply", "A fresh public reply."), ct);
        var (credential, token) = ParticipantCredential.Issue(app.HumanParticipantId, "Notification reader", 1, [ParticipationGrants.Read], DateTimeOffset.UtcNow);
        await credential.Save(ct);
        using var human = new HttpClient { BaseAddress = new Uri(app.Origin) };
        human.DefaultRequestHeaders.Authorization = new("Bearer", token);
        foreach (var client in new[] { app.Http, human })
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            var notifications = await client.GetFromJsonAsync<JsonElement>("/api/workspace/notifications", ct);
            while (!notifications.GetProperty("items").EnumerateArray().Any(item => item.GetProperty("id").GetString() == post.Id)
                   && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(100, ct);
                notifications = await client.GetFromJsonAsync<JsonElement>("/api/workspace/notifications", ct);
            }
            Assert.Contains(notifications.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == post.Id);
        }
        Assert.Null(await TangentMembership.Get(TangentMembership.Key("public-follow", app.AgentParticipantId), ct));
        Assert.Null(await TangentMembership.Get(TangentMembership.Key("public-follow", app.HumanParticipantId), ct));
        await hub.Participants.SetWatch(app.AgentParticipantId, "follow-thought", WatchMode.None, ct);
        var muted = await app.Http.GetFromJsonAsync<JsonElement>("/api/workspace/notifications", ct);
        Assert.DoesNotContain(muted.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == post.Id);
        await hub.Topics.SetReadAudience(app.OwnerParticipantId, "follow-thought", TopicReadAudience.Restricted, false, ct);
        var privateResult = await human.GetFromJsonAsync<JsonElement>("/api/workspace/notifications", ct);
        Assert.DoesNotContain(privateResult.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == post.Id);
    }
}
