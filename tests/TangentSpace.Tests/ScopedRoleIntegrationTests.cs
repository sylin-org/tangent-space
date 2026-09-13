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
}
