using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using TangentSpace.Authorization;
using TangentSpace.Rooms;
using TangentSpace.Tests.ExperienceIntegration;
using Xunit;

namespace TangentSpace.Tests;

public sealed class ScopedRoleIntegrationTests : IAsyncLifetime
{
    private ExperienceWebApp app = null!;

    public async ValueTask InitializeAsync() => app = await ExperienceWebApp.StartAsync();
    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    [Fact]
    public async Task Owners_receive_owner_memberships_and_topic_memberships_drive_capabilities()
    {
        var descriptor = await app.Http.GetAsync("/api/identity/scoped-roles/descriptor",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, descriptor.StatusCode);
        Assert.Contains(TangentRoleCapabilities.TopicManage,
            await descriptor.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var roleContext = app.Services.GetRequiredService<TangentRoleContext>();
        using var scope = app.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var host = TangentRoleScopes.HostScope;
        var tangent = TangentRoleScopes.TangentScope(ExperienceWebApp.TangentKey);
        var topic = TangentRoleScopes.TopicScope(ExperienceWebApp.TopicKey);

        using (roleContext.Bind(app.OwnerParticipantId))
        {
            var hostMembership = await engine.Memberships(app.OwnerParticipantId, host,
                TestContext.Current.CancellationToken);
            Assert.Contains($"tangent:{host.Type}:{host.Id}:owner", hostMembership.Roles);

            var tangentMembership = await engine.Memberships(app.OwnerParticipantId, tangent,
                TestContext.Current.CancellationToken);
            Assert.Contains($"tangent:{tangent.Type}:{tangent.Id}:owner", tangentMembership.Roles);

            Assert.True(await engine.Check(TangentRoleCapabilities.TopicManage, topic,
                ct: TestContext.Current.CancellationToken));
        }

        using (roleContext.Bind(app.AgentParticipantId))
        {
            Assert.True(await engine.Check(TangentRoleCapabilities.TopicReply, topic,
                ct: TestContext.Current.CancellationToken));
            Assert.False(await engine.Check(TangentRoleCapabilities.TopicManage, topic,
                ct: TestContext.Current.CancellationToken));
        }

        var rooms = app.Services.GetRequiredService<RoomGovernance>();
        var changed = await rooms.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            ExperienceWebApp.AgentDid, RoomRole.Manager, TestContext.Current.CancellationToken);
        Assert.True(changed.Accepted, changed.Reason);

        using (roleContext.Bind(app.AgentParticipantId))
            Assert.True(await engine.Check(TangentRoleCapabilities.TopicManage, topic,
                ct: TestContext.Current.CancellationToken));

        var demoted = await rooms.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            ExperienceWebApp.AgentDid, RoomRole.Reader, TestContext.Current.CancellationToken);
        Assert.True(demoted.Accepted, demoted.Reason);
        using (roleContext.Bind(app.AgentParticipantId))
            Assert.False(await engine.Check(TangentRoleCapabilities.TopicManage, topic,
                ct: TestContext.Current.CancellationToken));

        var restored = await rooms.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            ExperienceWebApp.AgentDid, RoomRole.Manager, TestContext.Current.CancellationToken);
        Assert.True(restored.Accepted, restored.Reason);
        using (roleContext.Bind(app.AgentParticipantId))
            Assert.True(await engine.Check(TangentRoleCapabilities.TopicManage, topic,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Protected_owner_memberships_reject_removal_and_non_owner_addition()
    {
        var host = TangentRoleScopes.HostScope;
        var tangent = TangentRoleScopes.TangentScope(ExperienceWebApp.TangentKey);
        var hostOwner = $"tangent:{host.Type}:{host.Id}:owner";
        var tangentOwner = $"tangent:{tangent.Type}:{tangent.Id}:owner";
        var roleContext = app.Services.GetRequiredService<TangentRoleContext>();
        using var scope = app.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();

        using (roleContext.Bind(app.OwnerParticipantId))
        {
            await Assert.ThrowsAsync<ScopedRoleAuthorizationException>(() =>
                engine.Remove(new ScopedRoleMember(host, app.OwnerParticipantId, hostOwner),
                    TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ScopedRoleAuthorizationException>(() =>
                engine.Add(new ScopedRoleMember(host, app.AgentParticipantId, hostOwner),
                    TestContext.Current.CancellationToken));
            Assert.False(await engine.Add(new ScopedRoleMember(host, app.OwnerParticipantId, hostOwner),
                TestContext.Current.CancellationToken));

            await Assert.ThrowsAsync<ScopedRoleAuthorizationException>(() =>
                engine.Add(new ScopedRoleMember(tangent, app.AgentParticipantId, tangentOwner),
                    TestContext.Current.CancellationToken));
            Assert.False(await engine.Add(new ScopedRoleMember(tangent, app.OwnerParticipantId, tangentOwner),
                TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ScopedRoleAuthorizationException>(() =>
                engine.Remove(new ScopedRoleMember(tangent, app.OwnerParticipantId, tangentOwner),
                    TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Ordinary_role_membership_is_an_idempotent_collection()
    {
        var host = TangentRoleScopes.HostScope;
        var roleContext = app.Services.GetRequiredService<TangentRoleContext>();
        using var scope = app.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();

        using (roleContext.Bind(app.OwnerParticipantId))
        {
            var role = await engine.Define(new(host, "Acceptance gardener", [],
                Id: "tangent:host:site:acceptance-gardener",
                Purpose: "Disposable membership collection evidence."),
                TestContext.Current.CancellationToken);
            var member = new ScopedRoleMember(host, app.AgentParticipantId, role.Id);

            Assert.True(await engine.Add(member, TestContext.Current.CancellationToken));
            Assert.False(await engine.Add(member, TestContext.Current.CancellationToken));

            var memberships = await engine.Memberships(app.AgentParticipantId, host,
                TestContext.Current.CancellationToken);
            Assert.Contains(role.Id, memberships.Roles);
            var members = await engine.Members(role.Id, host, pageSize: 10,
                ct: TestContext.Current.CancellationToken);
            Assert.Contains(members.Items, value => value.Subject == app.AgentParticipantId);

            Assert.True(await engine.Remove(member, TestContext.Current.CancellationToken));
            Assert.False(await engine.Remove(member, TestContext.Current.CancellationToken));
            Assert.False(await engine.Remove(new ScopedRoleMember(host, app.HumanParticipantId, role.Id),
                TestContext.Current.CancellationToken));

            memberships = await engine.Memberships(app.AgentParticipantId, host,
                TestContext.Current.CancellationToken);
            Assert.DoesNotContain(role.Id, memberships.Roles);
            members = await engine.Members(role.Id, host, pageSize: 10,
                ct: TestContext.Current.CancellationToken);
            Assert.DoesNotContain(members.Items, value => value.Subject == app.AgentParticipantId);
        }
    }
}
