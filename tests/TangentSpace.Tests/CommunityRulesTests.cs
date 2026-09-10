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
    private const string HostOwner = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Member = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Visitor = "did:plc:cccccccccccccccccccccccc";
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static TangentSite Site() => TangentSite.Establish(new SiteOptions { Name = "Host", OwnerDid = HostOwner }, HostOwner, Now);

    [Fact]
    public void A_new_tangent_is_owned_by_the_configured_host_owner_and_has_a_card()
    {
        var tangent = TangentCommunity.Create(Site(), HostOwner, "kintsugi", " Kintsugi Architecture ", " A welcoming workshop ",
            "Keep the seams visible.", "#e19b53", "/art/kintsugi.png", Now);

        Assert.Equal("kintsugi", tangent.Id);
        Assert.Equal("Kintsugi Architecture", tangent.Name);
        Assert.Equal(HostOwner, tangent.OwnerDid);
        Assert.False(tangent.OpenToSignedIn);
        Assert.True(tangent.IsOwner(HostOwner));
        Assert.False(tangent.IsOwner(Member));
    }

    [Fact]
    public void Community_membership_is_required_before_a_signed_in_channel_is_visible()
    {
        var site = Site();
        var tangent = TangentCommunity.Create(site, HostOwner, "kintsugi", "Kintsugi", "", "", "", "", Now);
        var room = Room.Create(site, HostOwner, "kintsugi-lounge", "Lounge", RoomAdmission.SignedIn, Now, tangent.Id, tangent);
        room.CompleteSpace(site, HostOwner, room.PolicyRevision, "at://did:plc:aaaaaaaaaaaaaaaaaaaaaaaa/chat.tangent.space/kintsugi-lounge", Now, tangent);

        var outsider = room.CurrentPolicy(site, Visitor, null, tangent: tangent);
        Assert.False(outsider.CanRead);
        Assert.Equal("community-membership-required", outsider.Reason);

        var membership = TangentMembership.Assign(tangent, Member, TangentRole.Member, HostOwner, Now);
        var admitted = room.CurrentPolicy(site, Member, null, tangent: tangent, tangentMembership: membership);
        Assert.True(admitted.CanRead);
        Assert.True(admitted.CanWrite);
    }

    [Fact]
    public void Legacy_home_rooms_keep_their_key_and_signed_in_admission_behavior()
    {
        var site = Site();
        var room = Room.Create(site, HostOwner, "lounge", "Lounge", RoomAdmission.SignedIn, Now);
        room.CompleteSpace(site, HostOwner, room.PolicyRevision, "at://did:plc:aaaaaaaaaaaaaaaaaaaaaaaa/chat.tangent.space/lounge", Now);

        Assert.Equal(TangentCommunity.HomeKey, room.TangentKey);
        Assert.True(room.CurrentPolicy(site, Visitor, null).CanRead);
    }

    [Fact]
    public void Invitation_only_channels_require_the_existing_room_grant_after_community_membership()
    {
        var site = Site();
        var tangent = TangentCommunity.Create(site, HostOwner, "kintsugi", "Kintsugi", "", "", "", "", Now);
        var communityMember = TangentMembership.Assign(tangent, Member, TangentRole.Member, HostOwner, Now);
        var room = Room.Create(site, HostOwner, "kintsugi-private", "Private", RoomAdmission.InvitationOnly, Now, tangent.Id, tangent);
        room.CompleteSpace(site, HostOwner, room.PolicyRevision, "at://did:plc:aaaaaaaaaaaaaaaaaaaaaaaa/chat.tangent.space/kintsugi-private", Now, tangent);

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

    [Fact]
    public void A_pending_channel_remains_manageable_by_its_owner_before_native_provisioning()
    {
        var site = Site();
        var tangent = TangentCommunity.Create(site, HostOwner, "kintsugi", "Kintsugi", "", "", "", "", Now);
        var room = Room.Create(site, HostOwner, "kintsugi-pending", "Pending", RoomAdmission.InvitationOnly, Now, tangent.Id, tangent);

        var policy = room.CurrentPolicy(site, HostOwner, null, tangent: tangent);
        Assert.False(policy.CanRead);
        Assert.True(policy.CanManage);
        Assert.Equal("space-pending", policy.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stale_expected_participant_rejects_community_reads_and_mutations_before_service_access(bool multipleValues)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AtprotoClaimTypes.Did, HostOwner)], "cookie"))
        };
        context.Request.Headers["X-Tangent-Participant"] = multipleValues ? new StringValues([HostOwner, Member]) : Member;
        var controller = new TangentsController(null!) { ControllerContext = new ControllerContext { HttpContext = context } };

        var read = await controller.List(page: 1, ct: TestContext.Current.CancellationToken);
        var mutation = await controller.SetMembership("kintsugi", Member, new ChangeTangentMembershipRequest(TangentRole.Member), TestContext.Current.CancellationToken);

        Assert.IsType<ConflictObjectResult>(read);
        Assert.IsType<ConflictObjectResult>(mutation);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }
}
