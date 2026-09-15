using TangentSpace.Communities;
using TangentSpace.Communities.Web;
using TangentSpace.Rooms;
using TangentSpace.Site;
using System.Security.Claims;
using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace TangentSpace.Tests;

public sealed class CommunityRulesTests
{
    private const string HostOwnerDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly string HostOwner = TangentSpace.Participants.Participant.NewIdentifier();
    private static readonly string Member = TangentSpace.Participants.Participant.NewIdentifier();
    private static readonly string Visitor = TangentSpace.Participants.Participant.NewIdentifier();
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static TangentSite Site() => TangentSite.Establish(new SiteOptions { Name = "Host", OwnerDid = HostOwnerDid }, HostOwnerDid, HostOwner, Now);

    [Fact]
    public void A_new_tangent_is_owned_by_the_configured_host_owner_and_has_a_card()
    {
        var tangent = TangentCommunity.Create(Site(), HostOwner, "kintsugi", " Kintsugi Architecture ", " A welcoming workshop ",
            "Keep the seams visible.", "#e19b53", "/art/kintsugi.png", Now);

        Assert.Equal("kintsugi", tangent.Id);
        Assert.Equal("Kintsugi Architecture", tangent.Name);
        Assert.Equal(HostOwner, tangent.OwnerParticipantId);
        Assert.False(tangent.OpenToSignedIn);
        Assert.True(tangent.IsOwner(HostOwner));
        Assert.False(tangent.IsOwner(Member));
    }

    [Fact]
    public void Community_membership_is_required_before_a_signed_in_channel_is_visible()
    {
        var site = Site();
        var tangent = TangentCommunity.Create(site, HostOwner, "kintsugi", "Kintsugi", "", "", "", "", Now);
        var room = Room.Create(site, HostOwner, "kintsugi-lounge", "Lounge", RoomAdmission.SignedIn, Now, tangent);

        var outsider = room.CurrentPolicy(site, Visitor, null, tangent: tangent);
        Assert.False(outsider.CanRead);
        Assert.Equal("community-membership-required", outsider.Reason);

        var membership = TangentMembership.Assign(tangent, Member, TangentRole.Member, HostOwner, Now);
        var admitted = room.CurrentPolicy(site, Member, null, tangent: tangent, tangentMembership: membership);
        Assert.True(admitted.CanRead);
        Assert.True(admitted.CanWrite);
    }

    [Fact]
    public void A_topic_whose_Tangent_is_missing_admits_no_one()
    {
        var site = Site();
        var tangent = TangentCommunity.Create(site, HostOwner, "kintsugi", "Kintsugi", "", "", "", "", Now);
        var room = Room.Create(site, HostOwner, "kintsugi-lounge", "Lounge", RoomAdmission.SignedIn, Now, tangent);

        var policy = room.CurrentPolicy(site, HostOwner, null);

        Assert.False(policy.CanRead || policy.CanManage);
        Assert.Equal("tangent-not-found", policy.Reason);
    }

    [Fact]
    public void Invitation_only_channels_require_the_existing_room_grant_after_community_membership()
    {
        var site = Site();
        var tangent = TangentCommunity.Create(site, HostOwner, "kintsugi", "Kintsugi", "", "", "", "", Now);
        var communityMember = TangentMembership.Assign(tangent, Member, TangentRole.Member, HostOwner, Now);
        var room = Room.Create(site, HostOwner, "kintsugi-private", "Private", RoomAdmission.InvitationOnly, Now, tangent);

        Assert.False(room.CurrentPolicy(site, Member, null, tangent: tangent, tangentMembership: communityMember).CanRead);
        var channelMember = room.ChangeMembership(site, HostOwner, null, Member, null, RoomRole.Member, Now, tangent);
        Assert.True(room.CurrentPolicy(site, Member, channelMember, tangent: tangent, tangentMembership: communityMember).CanRead);
    }

    [Fact]
    public void Card_changes_remain_with_the_community_owner()
    {
        var tangent = TangentCommunity.Create(Site(), HostOwner, "kintsugi", "Kintsugi", "", "", "", "", Now);
        Assert.Equal(TangentDenial.Forbidden, Assert.Throws<TangentRuleViolation>(() =>
            tangent.Change(Member, "Other", null, null, null, null, Now)).Denial);

        tangent.Change(HostOwner, "Kintsugi Home", null, "Mend and continue.", "#af7e46", "/art/home.png", Now.AddMinutes(1));
        Assert.Equal("Kintsugi Home", tangent.Name);
        Assert.Equal("Mend and continue.", tangent.Motto);
        Assert.Equal(2, tangent.PolicyRevision);
    }

    [Fact]
    public void Removed_membership_overrides_open_community_admission_and_invalid_roles_are_rejected()
    {
        var tangent = TangentCommunity.Create(Site(), HostOwner, "homework", "Homework", "", "", "", "", Now);
        tangent.OpenToSignedIn = true;
        var removed = TangentMembership.Assign(tangent, Member, TangentRole.Removed, HostOwner, Now);
        Assert.False(tangent.CanParticipate(Member, removed));

        var malformed = TangentMembership.Assign(tangent, Member, TangentRole.Member, HostOwner, Now);
        malformed.Role = (TangentRole)99;
        Assert.Equal(TangentDenial.MembershipMismatch,
            Assert.Throws<TangentRuleViolation>(() => tangent.CanParticipate(Member, malformed)).Denial);
    }
}
