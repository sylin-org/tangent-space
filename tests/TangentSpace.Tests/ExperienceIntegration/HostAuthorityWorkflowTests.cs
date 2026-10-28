using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Application;
using Tangent.Community;
using Tangent.Identity;
using Tangent.Infrastructure;
using Tangent.Spaces;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class HostAuthorityWorkflowTests
{
    [Fact]
    public async Task Protected_host_arrival_explains_identity_and_admission_without_private_metadata()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var hub = app.Services.GetRequiredService<TangentServer>();
        using var owner = Client(app, app.OwnerToken);
        using var anonymous = Client(app);
        (await owner.PatchAsJsonAsync("/api/server", new
        {
            name = "Private Host identity", welcomeMessage = "Private welcome text", motd = "Private announcement",
            byline = "Private byline", coverImageUrl = "https://private.test/cover.png", markImageUrl = "https://private.test/mark.png"
        }, ct)).EnsureSuccessStatusCode();
        (await owner.PatchAsJsonAsync("/api/space/admission", new { mode = "Approval" }, ct)).EnsureSuccessStatusCode();
        var pending = await Arrival(ct);
        using var arrival = Client(app, pending.Token);

        var publicWelcome = await Read(arrival, "/api/site", ct);
        Assert.True(publicWelcome.GetProperty("workspaceAvailable").GetBoolean());
        Assert.Equal("Private Host identity", publicWelcome.GetProperty("name").GetString());
        Assert.False((await Read(arrival, "/api/space/admission", ct)).GetProperty("admitted").GetBoolean());
        (await arrival.PostAsJsonAsync("/api/space/admission/join", new { }, ct)).EnsureSuccessStatusCode();
        await hub.Space.SetAccess(app.OwnerParticipantId, new AccessMap { See = [AccessTokens.Authenticated] }, ct);

        var signedOut = await Doorstep(anonymous, ct);
        Assert.Equal("sign_in", signedOut.GetProperty("onboarding").GetString());
        Assert.True(!signedOut.TryGetProperty("participant", out var absent) || absent.ValueKind == JsonValueKind.Null);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync("/api/server", ct)).StatusCode);

        var signedIn = await Doorstep(arrival, ct);
        Assert.Equal("complete", signedIn.GetProperty("onboarding").GetString());
        Assert.Equal(pending.Participant.Id, signedIn.GetProperty("participant").GetProperty("participantRef").GetString());
        Assert.False(signedIn.GetProperty("participant").GetProperty("isOwner").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await arrival.GetAsync("/api/server", ct)).StatusCode);
        var admission = await Read(arrival, "/api/space/admission", ct);
        Assert.Equal("Approval", admission.GetProperty("mode").GetString());
        Assert.Equal("pending", admission.GetProperty("status").GetString());
        Assert.False(admission.GetProperty("admitted").GetBoolean());
        Assert.DoesNotContain(AccessTokens.Authenticated, await hub.Roles.Bag(pending.Participant.Id, ct));
        Assert.False((await hub.Topics.Describe(pending.Participant.Id, ExperienceWebApp.TopicKey, ct))?.CanRead ?? false);

        var admitted = await Read(app.Http, "/api/site", ct);
        Assert.True(admitted.GetProperty("workspaceAvailable").GetBoolean());
        Assert.Equal("Private Host identity", admitted.GetProperty("name").GetString());
        Assert.False(admitted.GetProperty("server").GetProperty("canManageParticipants").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await app.Http.GetAsync("/api/server", ct)).StatusCode);
        Assert.True((await Read(owner, "/api/site", ct)).GetProperty("server").GetProperty("canManageParticipants").GetBoolean());
        (await owner.PostAsJsonAsync("/api/space/admission/requests/" + pending.Participant.Id, new { accept = true }, ct)).EnsureSuccessStatusCode();
        Assert.True((await Read(arrival, "/api/site", ct)).GetProperty("workspaceAvailable").GetBoolean());
        Assert.Equal("Private Host identity", (await Read(arrival, "/api/server", ct)).GetProperty("name").GetString());

        async Task<JsonElement> Doorstep(HttpClient client, CancellationToken cancellationToken)
        {
            using var response = await client.GetAsync("/api/site", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            foreach (var privateValue in new[] { "Private Host identity", "Private welcome text", "Private announcement", "Private byline",
                "https://private.test/cover.png", "https://private.test/mark.png", "Experience Integration Space",
                app.OwnerParticipantId, ExperienceWebApp.OwnerDid, "Project Z", "Workshop" })
                Assert.DoesNotContain(privateValue, body);
            using var document = JsonDocument.Parse(body);
            var welcome = document.RootElement.Clone();
            Assert.False(welcome.GetProperty("workspaceAvailable").GetBoolean());
            Assert.Equal("Tangent Space", welcome.GetProperty("name").GetString());
            Assert.Empty(welcome.GetProperty("topics").GetProperty("topics").EnumerateArray());
            foreach (var property in new[] { "server", "initialTangent", "initialTangentKey" })
                Assert.True(!welcome.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null);
            return welcome;
        }
    }

    [Fact]
    public async Task Host_owner_remains_human_while_nonowners_can_declare_and_own_agent_scopes()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var hub = app.Services.GetRequiredService<TangentServer>();
        using var owner = Client(app, app.OwnerToken);
        foreach (var classification in new[] { "Agent", "Undeclared" })
        {
            using var response = await owner.PatchAsJsonAsync("/api/participants/me", new { classification }, ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Host owner must remain declared Human", await response.Content.ReadAsStringAsync(ct));
            using var fresh = EntityContext.NoCache();
            var participant = (await Participant.Get(app.OwnerParticipantId, ct))!;
            Assert.Equal(ParticipantClassification.Human, participant.Classification);
            Assert.True(participant.HasDeclared);
            Assert.False(participant.WasDeclaredAgent);
            Assert.True((await Space.Get(TangentConstants.SpaceId, ct))!.IsOwner(participant.Id));
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.Participants.DeclareClassification(app.OwnerParticipantId,
            app.OwnerParticipantId, ParticipantClassification.Agent, ct));
        foreach (var classification in new[] { "Agent", "Undeclared", "Human", "Agent" })
        {
            using var response = await app.Http.PatchAsJsonAsync("/api/participants/me", new { classification }, ct);
            response.EnsureSuccessStatusCode();
            using var fresh = EntityContext.NoCache();
            Assert.Equal(classification, (await Participant.Get(app.AgentParticipantId, ct))!.Classification.ToString());
        }
        (await owner.PatchAsJsonAsync("/api/server", new { allowAgentTangentOwnership = true }, ct)).EnsureSuccessStatusCode();
        var roles = app.Services.GetRequiredService<RoleCollection>();
        await roles.Define("role:agent-creator", "Agent creator", [TangentPermissions.CreateTangents, TangentPermissions.CreateTopics], ct: ct);
        await roles.Add("role:agent-creator", app.AgentParticipantId, ct);
        await hub.Tangents.Create(app.AgentParticipantId, "agent-garden", "Agent garden", null, null, null, null, ct);
        await hub.Tangents.CreateTopic(app.AgentParticipantId, "agent-garden", "agent-notes", "Agent notes", null, ct);
        using (EntityContext.NoCache())
        {
            Assert.Equal(app.AgentParticipantId, (await Tangent.Community.Tangent.Get("agent-garden", ct))!.OwnerParticipantId);
            Assert.Equal(app.AgentParticipantId, (await Topic.Get("agent-notes", ct))!.CreatorParticipantId);
            Assert.Equal(ParticipantClassification.Human, (await Participant.Get(app.OwnerParticipantId, ct))!.Classification);
        }
    }

    [Fact]
    public async Task Delegated_people_stewardship_applies_to_admission_and_directory_until_revoked()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var roles = app.Services.GetRequiredService<RoleCollection>();
        const string role = "role:admission-steward";
        await roles.Define(role, "Admission steward", [TangentPermissions.ManageParticipants], ct: ct);
        using var owner = Client(app, app.OwnerToken);
        (await owner.PatchAsJsonAsync("/api/space/admission", new { mode = "Approval" }, ct)).EnsureSuccessStatusCode();
        var accepted = await Pending(app, ct);
        var refused = await Pending(app, ct);
        using var steward = Client(app, await Credential(app.AgentParticipantId, true, ct));
        var lookup = "/api/people/lookup?identifier=" + Uri.EscapeDataString(ExperienceWebApp.HumanHandle);
        await Denied(steward, accepted.Participant.Id, lookup, ct);
        await Capability(steward, false, ct);

        await roles.Add(role, app.AgentParticipantId, ct);
        await Capability(steward, true, ct);
        var requests = await Read(steward, "/api/space/admission/requests", ct);
        Assert.Equal(2, requests.GetProperty("items").GetArrayLength());
        Assert.Equal(accepted.Participant.Id, requests.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Contains((await Read(steward, "/api/people", ct)).GetProperty("people").EnumerateArray(),
            person => person.GetProperty("id").GetString() == refused.Participant.Id);
        Assert.Equal(app.HumanParticipantId, (await Read(steward, lookup, ct)).GetProperty("id").GetString());
        Assert.False((await Read(steward, "/api/server", ct)).GetProperty("canManage").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await steward.PatchAsJsonAsync("/api/space/admission", new { mode = "Open" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await steward.PatchAsJsonAsync("/api/server", new { name = "Unauthorized identity" }, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await steward.GetAsync("/api/server/access", ct)).StatusCode);
        (await steward.PostAsJsonAsync("/api/space/admission/requests/" + accepted.Participant.Id, new { accept = true }, ct)).EnsureSuccessStatusCode();
        (await steward.PostAsJsonAsync("/api/space/admission/requests/" + refused.Participant.Id, new { accept = false }, ct)).EnsureSuccessStatusCode();
        using (EntityContext.NoCache())
        {
            var acceptedMembership = (await SpaceMembership.Get(accepted.Participant.Id, ct))!;
            Assert.Equal("accepted", acceptedMembership.Status);
            Assert.Equal(app.AgentParticipantId, acceptedMembership.DecidedBy);
            Assert.True(await SpaceAdmission.Admitted(await Space.Get(TangentConstants.SpaceId, ct), accepted.Participant.Id, ct));
            Assert.Equal("refused", (await SpaceMembership.Get(refused.Participant.Id, ct))!.Status);
            Assert.False(await SpaceAdmission.Admitted(await Space.Get(TangentConstants.SpaceId, ct), refused.Participant.Id, ct));
        }
        var awaiting = await Pending(app, ct);
        await roles.Remove(role, app.AgentParticipantId, ct);
        await Capability(steward, false, ct);
        await Denied(steward, awaiting.Participant.Id, lookup, ct);
        using (EntityContext.NoCache()) Assert.Equal("pending", (await SpaceMembership.Get(awaiting.Participant.Id, ct))!.Status);
        await Capability(owner, true, ct);
        Assert.Equal("Approval", (await Read(owner, "/api/space/admission", ct)).GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Participant_management_requires_manage_transport_current_admission_and_active_identity()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var roles = app.Services.GetRequiredService<RoleCollection>();
        var access = app.Services.GetRequiredService<TangentRoleAccess>();
        await roles.Define("role:limited-steward", "Limited steward", [TangentPermissions.ManageParticipants], ct: ct);
        await roles.Add("role:limited-steward", app.AgentParticipantId, ct);
        using var owner = Client(app, app.OwnerToken);
        (await owner.PatchAsJsonAsync("/api/space/admission", new { mode = "Approval" }, ct)).EnsureSuccessStatusCode();
        var pending = await Pending(app, ct);
        var lookup = "/api/people/lookup?identifier=" + Uri.EscapeDataString(ExperienceWebApp.HumanHandle);
        using var limited = Client(app, await Credential(app.AgentParticipantId, false, ct));
        await Capability(limited, false, ct);
        await Denied(limited, pending.Participant.Id, lookup, ct);
        Assert.True(await access.CanManageParticipants(app.AgentParticipantId, ct));

        await roles.Add("role:limited-steward", pending.Participant.Id, ct);
        using var unadmitted = Client(app, await Credential(pending.Participant.Id, true, ct));
        await Capability(unadmitted, false, ct);
        await Denied(unadmitted, pending.Participant.Id, lookup, ct);
        Assert.False(await access.CanManageParticipants(pending.Participant.Id, ct));
        using (EntityContext.NoCache())
        {
            var participant = (await Participant.Get(app.AgentParticipantId, ct))!;
            participant.IsSuspended = true;
            await participant.Save(ct);
        }
        Assert.False(await access.CanManageParticipants(app.AgentParticipantId, ct));
        using var suspended = Client(app, await Credential(app.AgentParticipantId, true, ct));
        var status = (await suspended.GetAsync("/api/space/admission/requests", ct)).StatusCode;
        Assert.True(status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        using (EntityContext.NoCache()) Assert.Equal("pending", (await SpaceMembership.Get(pending.Participant.Id, ct))!.Status);
    }

    private static HttpClient Client(ExperienceWebApp app, string? token = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(app.Origin) };
        if (token is not null) client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Read(HttpClient client, string route, CancellationToken ct)
    {
        using var response = await client.GetAsync(route, ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return document.RootElement.Clone();
    }

    private static async Task Capability(HttpClient client, bool expected, CancellationToken ct)
    {
        Assert.Equal(expected, (await Read(client, "/api/server", ct)).GetProperty("canManageParticipants").GetBoolean());
        Assert.Equal(expected, (await Read(client, "/api/site", ct)).GetProperty("server").GetProperty("canManageParticipants").GetBoolean());
    }

    private static async Task Denied(HttpClient client, string pendingId, string lookup, CancellationToken ct)
    {
        foreach (var route in new[] { "/api/space/admission/requests", "/api/people", lookup })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route, ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/space/admission/requests/" + pendingId,
            new { accept = true }, ct)).StatusCode);
    }

    private static async Task<string> Credential(string participantId, bool manage, CancellationToken ct)
    {
        var grants = new List<string> { ParticipationGrants.Welcome, ParticipationGrants.Read };
        if (manage) grants.Add(ParticipationGrants.Manage);
        var (credential, token) = ParticipantCredential.Issue(participantId, "Host authority fixture", 1, grants.ToArray(), DateTimeOffset.UtcNow);
        using var fresh = EntityContext.NoCache();
        await credential.Save(ct);
        return token;
    }

    private static async Task<(Participant Participant, string Token)> Arrival(CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("n")[..24];
        var (participant, identities) = Participant.Enroll("did:plc:" + suffix, suffix + ".authority.test", DateTimeOffset.UtcNow.AddMinutes(1));
        using var fresh = EntityContext.NoCache();
        await participant.Save(ct);
        foreach (var identity in identities) await identity.Save(ct);
        await new ParticipantProfileSnapshot
        {
            Id = "did:plc:" + suffix, DisplayName = "Fixture arrival", CapturedAt = DateTimeOffset.UtcNow,
            RetryAfter = DateTimeOffset.UtcNow.AddDays(1)
        }.Save(ct);
        return (participant, await Credential(participant.Id, false, ct));
    }

    private static async Task<(Participant Participant, string Token)> Pending(ExperienceWebApp app, CancellationToken ct)
    {
        var arrival = await Arrival(ct);
        using var client = Client(app, arrival.Token);
        using var response = await client.PostAsJsonAsync("/api/space/admission/join", new { }, ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal("pending", document.RootElement.GetProperty("status").GetString());
        return arrival;
    }
}
