using TangentSpace.Communities;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using TangentSpace.Site;
using Xunit;

namespace TangentSpace.Tests;

public sealed class RoomSettingsAuthorityTests
{
    private const string OwnerDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Owner = Participant.NewIdentifier();
    private static readonly string Creator = Participant.NewIdentifier();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Removed_creator_cannot_bypass_current_settings_authority(bool removedFromTopic)
    {
        var (site, tangent, room) = Place();
        var communityRole = Membership(tangent, removedFromTopic ? TangentRole.Admin : TangentRole.Removed);
        var topicRole = removedFromTopic
            ? RoomMembership.Assign(room, Creator, RoomRole.Removed, Owner, Now)
            : null;
        var beforeRevision = room.PolicyRevision;
        var beforeUpdatedAt = room.UpdatedAt;

        Assert.False(room.CurrentPolicy(site, Creator, topicRole, tangent: tangent,
            tangentMembership: communityRole).CanManage);

        var denial = Assert.Throws<RoomRuleViolation>(() => room.ChangeSettings(site, Creator, topicRole,
            true, true, "Unauthorized rename", "Unauthorized topic", Now.AddMinutes(1), tangent, communityRole));

        Assert.Equal(RoomDenial.Forbidden, denial.Denial);
        Assert.Equal("Workshop", room.Title);
        Assert.Empty(room.Topic);
        Assert.False(room.AllowPostEditing);
        Assert.False(room.IsLocked);
        Assert.Equal(beforeRevision, room.PolicyRevision);
        Assert.Equal(beforeUpdatedAt, room.UpdatedAt);
    }

    [Fact]
    public void Current_creator_management_and_human_owner_management_still_work()
    {
        var (site, tangent, room) = Place();
        var communityRole = Membership(tangent, TangentRole.Admin);
        room.ChangeSettings(site, Creator, null, true, false, "Updated", null,
            Now.AddMinutes(1), tangent, communityRole);
        Assert.Equal("Updated", room.Title);
        Assert.True(room.AllowPostEditing);

        room.ChangeSettings(site, Owner, null, false, true, null, "Owner settings",
            Now.AddMinutes(2), tangent);
        Assert.True(room.IsLocked);
        Assert.Equal("Owner settings", room.Topic);
    }

    private static (TangentSite Site, TangentCommunity Tangent, Room Room) Place()
    {
        var site = TangentSite.Establish(new SiteOptions { Name = "Tangent", OwnerDid = OwnerDid },
            OwnerDid, Owner, Now);
        var tangent = new TangentCommunity
        {
            Id = "stewardship", OwnerParticipantId = Owner, Name = "Stewardship",
        };
        var room = Room.CreateDelegated(site, Creator, "stewardship-work", "Workshop",
            RoomAdmission.SignedIn, Now, tangent);
        return (site, tangent, room);
    }

    private static TangentMembership Membership(TangentCommunity tangent, TangentRole role)
        => new() { Id = TangentMembership.Key(tangent.Id, Creator), TangentKey = tangent.Id,
            ParticipantId = Creator, Role = role };
}
