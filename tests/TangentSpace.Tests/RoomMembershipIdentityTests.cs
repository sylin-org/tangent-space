using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using TangentSpace.Tests.ExperienceIntegration;
using Xunit;

namespace TangentSpace.Tests;

[Collection("Experience integration")]
public sealed class RoomMembershipIdentityTests : IAsyncLifetime
{
    private ExperienceWebApp app = null!;
    private RoomGovernance Rooms => app.Services.GetRequiredService<RoomGovernance>();
    private const string Topic = ExperienceWebApp.TopicKey;

    public async ValueTask InitializeAsync() => app = await ExperienceWebApp.StartAsync();
    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    [Theory]
    [InlineData("did")]
    [InlineData("handle")]
    [InlineData("local")]
    [InlineData("participant")]
    public async Task Resolved_target_is_shared_by_assignment_update_audit_and_owner_protection(string form)
    {
        var targetId = app.HumanParticipantId;
        var supplied = Identifier(form, targetId, ExperienceWebApp.HumanDid, ExperienceWebApp.HumanHandle);
        var membershipId = RoomMembership.Key(Topic, targetId);
        using var fresh = EntityContext.NoCache();
        Assert.Null(await RoomMembership.Get(membershipId));

        foreach (var role in new[] { RoomRole.Reader, RoomRole.Member })
        {
            var result = await Rooms.SetMembership(app.OwnerParticipantId, Topic, supplied, role, CancellationToken.None);
            Assert.True(result.Accepted, result.Reason);
            var membership = await RoomMembership.Get(membershipId);
            Assert.NotNull(membership);
            Assert.Equal(targetId, membership.ParticipantId);
            Assert.Equal(Topic, membership.RoomKey);
            Assert.Equal(role, membership.Role);
            Assert.Equal(app.OwnerParticipantId, membership.ChangedByParticipantId);
            Assert.Equal(result.PolicyRevision, membership.PolicyRevision);
            if (supplied != targetId)
                Assert.Null(await RoomMembership.Get(RoomMembership.Key(Topic, supplied)));

            var audit = await RoomAudit.Get(result.AuditId);
            Assert.NotNull(audit);
            Assert.True(audit.Accepted);
            Assert.Equal(targetId, audit.TargetParticipantId);
            Assert.Equal(app.OwnerParticipantId, audit.ActorParticipantId);
            Assert.Equal(role, audit.RequestedRole);
        }

        var before = await Room.Get(Topic);
        Assert.NotNull(before);
        var owner = Identifier(form, app.OwnerParticipantId, ExperienceWebApp.OwnerDid, "owner.experience.test");
        var denied = await Rooms.SetMembership(app.OwnerParticipantId, Topic, owner, RoomRole.Removed, CancellationToken.None);
        Assert.False(denied.Accepted);
        Assert.Equal(RoomDenial.Forbidden, denied.Denial);
        Assert.Null(await RoomMembership.Get(RoomMembership.Key(Topic, app.OwnerParticipantId)));
        Assert.Equal(before.PolicyRevision, (await Room.Get(Topic))!.PolicyRevision);
        Assert.Equal(RoomRole.Member, (await RoomMembership.Get(membershipId))!.Role);
        var deniedAudit = await RoomAudit.Get(denied.AuditId);
        Assert.NotNull(deniedAudit);
        Assert.False(deniedAudit.Accepted);
        Assert.Equal(app.OwnerParticipantId, deniedAudit.TargetParticipantId);
    }

    private static string Identifier(string form, string participantId, string did, string handle) => form switch
    {
        "did" => did,
        "handle" => " @" + handle.ToUpperInvariant() + " ",
        "local" => ParticipantIdentity.InternalValue(participantId),
        "participant" => participantId,
        _ => throw new ArgumentOutOfRangeException(nameof(form))
    };
}
