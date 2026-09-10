using TangentSpace.Activity;
using Xunit;

namespace TangentSpace.Tests;

public sealed class ActivityPrivacyTests
{
    [Fact]
    public void Room_member_does_not_receive_another_participants_membership_target()
    {
        var entry = new ActivityJournal { Sequence = 1, Kind = ActivityKind.MembershipChanged, RoomKey = "private-room",
            TangentKey = "home", ActorDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", TargetDid = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb" };
        var recipient = ActivityService.EventFor("did:plc:cccccccccccccccccccccccc", entry);
        Assert.Null(recipient.TargetDid);
    }

    [Fact]
    public void Membership_target_can_see_their_own_change_marker()
    {
        const string target = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
        var entry = new ActivityJournal { Sequence = 1, Kind = ActivityKind.MembershipChanged, RoomKey = "private-room",
            TangentKey = "home", ActorDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa", TargetDid = target };
        Assert.Equal(target, ActivityService.EventFor(target, entry).TargetDid);
    }
}
