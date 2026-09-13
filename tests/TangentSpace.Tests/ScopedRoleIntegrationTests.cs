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
    public async Task Owners_receive_durable_owner_roles_and_topic_memberships_drive_capabilities()
    {
        var descriptor = await app.Http.GetAsync("/api/identity/scoped-roles/descriptor",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, descriptor.StatusCode);
        Assert.Contains(TangentRoleCapabilities.TopicManage,
            await descriptor.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var host = TangentRoleScopes.HostScope;
        var ownerRoleId = $"tangent:{host.Type}:{host.Id}:owner";
        var ownerBindingId = ScopedRoleBinding.KeyFor(TangentRoleScopes.Tenant, app.OwnerParticipantId, ownerRoleId, host);
        var binding = await ScopedRoleBinding.Get(ownerBindingId, TestContext.Current.CancellationToken);

        Assert.NotNull(binding);
        Assert.False(binding.Revoked);
        Assert.Equal(ScopedRolePropagation.Descendants, binding.Propagation);
        Assert.Equal(app.OwnerParticipantId, binding.Subject);

        var tangent = TangentRoleScopes.TangentScope(ExperienceWebApp.TangentKey);
        var tangentOwnerRoleId = $"tangent:{tangent.Type}:{tangent.Id}:owner";
        var tangentOwnerBindingId = ScopedRoleBinding.KeyFor(TangentRoleScopes.Tenant,
            app.OwnerParticipantId, tangentOwnerRoleId, tangent);
        var tangentOwner = await ScopedRoleBinding.Get(tangentOwnerBindingId, TestContext.Current.CancellationToken);
        Assert.NotNull(tangentOwner);
        Assert.False(tangentOwner.Revoked);
        Assert.Equal(ScopedRolePropagation.Descendants, tangentOwner.Propagation);

        var roleContext = app.Services.GetRequiredService<TangentRoleContext>();
        using var scope = app.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();
        var topic = TangentRoleScopes.TopicScope(ExperienceWebApp.TopicKey);

        using (roleContext.Bind(app.OwnerParticipantId))
            Assert.True(await engine.Check(TangentRoleCapabilities.TopicManage, topic, ct: TestContext.Current.CancellationToken));

        using (roleContext.Bind(app.AgentParticipantId))
        {
            Assert.True(await engine.Check(TangentRoleCapabilities.TopicReply, topic, ct: TestContext.Current.CancellationToken));
            Assert.False(await engine.Check(TangentRoleCapabilities.TopicManage, topic, ct: TestContext.Current.CancellationToken));
        }

        var rooms = app.Services.GetRequiredService<RoomGovernance>();
        var changed = await rooms.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            ExperienceWebApp.AgentDid, RoomRole.Manager, TestContext.Current.CancellationToken);
        Assert.True(changed.Accepted, changed.Reason);

        using (roleContext.Bind(app.AgentParticipantId))
            Assert.True(await engine.Check(TangentRoleCapabilities.TopicManage, topic, ct: TestContext.Current.CancellationToken));

        var demoted = await rooms.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            ExperienceWebApp.AgentDid, RoomRole.Reader, TestContext.Current.CancellationToken);
        Assert.True(demoted.Accepted, demoted.Reason);
        using (roleContext.Bind(app.AgentParticipantId))
            Assert.False(await engine.Check(TangentRoleCapabilities.TopicManage, topic, ct: TestContext.Current.CancellationToken));

        var restored = await rooms.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            ExperienceWebApp.AgentDid, RoomRole.Manager, TestContext.Current.CancellationToken);
        Assert.True(restored.Accepted, restored.Reason);
        using (roleContext.Bind(app.AgentParticipantId))
            Assert.True(await engine.Check(TangentRoleCapabilities.TopicManage, topic, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Owner_bindings_reject_non_owner_assignments_and_revocations()
    {
        var host = TangentRoleScopes.HostScope;
        var hostOwnerRoleId = $"tangent:{host.Type}:{host.Id}:owner";
        var hostOwnerBindingId = ScopedRoleBinding.KeyFor(TangentRoleScopes.Tenant, app.OwnerParticipantId, hostOwnerRoleId, host);
        var hostBinding = await ScopedRoleBinding.Get(hostOwnerBindingId, TestContext.Current.CancellationToken);

        var tangent = TangentRoleScopes.TangentScope(ExperienceWebApp.TangentKey);
        var tangentOwnerRoleId = $"tangent:{tangent.Type}:{tangent.Id}:owner";
        var tangentOwnerBindingId = ScopedRoleBinding.KeyFor(TangentRoleScopes.Tenant, app.OwnerParticipantId, tangentOwnerRoleId, tangent);
        var tangentBinding = await ScopedRoleBinding.Get(tangentOwnerBindingId, TestContext.Current.CancellationToken);

        Assert.NotNull(hostBinding);
        Assert.NotNull(tangentBinding);

        var roleContext = app.Services.GetRequiredService<TangentRoleContext>();
        using var scope = app.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();

        using (roleContext.Bind(app.OwnerParticipantId))
        {
            await Assert.ThrowsAsync<Koan.Identity.Roles.ScopedRoleAuthorizationException>(() =>
                engine.Revoke(hostOwnerBindingId, hostBinding!.Version, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<Koan.Identity.Roles.ScopedRoleAuthorizationException>(() =>
                engine.Assign(new(host, app.AgentParticipantId, hostOwnerRoleId, ScopedRolePropagation.Descendants),
                    TestContext.Current.CancellationToken));

            var canonicalReassigned = await engine.Assign(new(host, app.OwnerParticipantId, hostOwnerRoleId, ScopedRolePropagation.Descendants),
                TestContext.Current.CancellationToken);
            Assert.NotNull(canonicalReassigned);
            Assert.False(canonicalReassigned.Revoked);

            await Assert.ThrowsAsync<Koan.Identity.Roles.ScopedRoleAuthorizationException>(() =>
                engine.Assign(new(tangent, app.AgentParticipantId, tangentOwnerRoleId, ScopedRolePropagation.Descendants),
                    TestContext.Current.CancellationToken));
            var tangentReassigned = await engine.Assign(new(tangent, app.OwnerParticipantId, tangentOwnerRoleId, ScopedRolePropagation.Descendants),
                TestContext.Current.CancellationToken);
            Assert.NotNull(tangentReassigned);
            Assert.False(tangentReassigned.Revoked);
        }

        using (roleContext.Bind(app.OwnerParticipantId))
        {
            await Assert.ThrowsAsync<Koan.Identity.Roles.ScopedRoleAuthorizationException>(() =>
                engine.Revoke(tangentOwnerBindingId, tangentBinding!.Version, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Ordinary_role_membership_round_trip_persists_assignment_and_revocation()
    {
        var host = TangentRoleScopes.HostScope;
        var roleContext = app.Services.GetRequiredService<TangentRoleContext>();
        using var scope = app.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<RoleEngine>();

        using (roleContext.Bind(app.OwnerParticipantId))
        {
            var role = await engine.Define(new(host, "Acceptance gardener", [],
                Id: "tangent:host:site:acceptance-gardener",
                Purpose: "Disposable membership round-trip evidence."),
                TestContext.Current.CancellationToken);

            var assigned = await engine.Assign(new(host, app.AgentParticipantId, role.Id),
                TestContext.Current.CancellationToken);
            var persisted = await ScopedRoleBinding.Get(assigned.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(persisted);
            Assert.False(persisted.Revoked);
            Assert.Equal(app.AgentParticipantId, persisted.Subject);

            var revoked = await engine.Revoke(persisted.Id, persisted.Version,
                TestContext.Current.CancellationToken);
            Assert.True(revoked.Revoked);
            var persistedRevocation = await ScopedRoleBinding.Get(persisted.Id, TestContext.Current.CancellationToken);
            Assert.Null(persistedRevocation);
        }
    }
}
