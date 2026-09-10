using TangentSpace.Participants;
using TangentSpace.Site;
using Xunit;

namespace TangentSpace.Tests;

public sealed class ArrivalRulesTests
{
    private const string Owner = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Visitor = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly DateTimeOffset Started = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_first_visitor_cannot_claim_ownership()
    {
        var options = new SiteOptions { Name = "Our site", OwnerDid = Owner };
        Assert.Throws<InvalidOperationException>(() => TangentSite.Establish(options, Visitor, Started));
    }

    [Fact]
    public void An_unconfigured_site_can_be_claimed_by_a_verified_account()
    {
        var site = TangentSite.Establish(new SiteOptions(), Visitor, Started);
        Assert.Equal(Visitor, site.OwnerDid);
        site.CheckConfiguredOwner(new SiteOptions());
        Assert.Throws<ArgumentException>(() => TangentSite.Establish(new SiteOptions(), "visitor.test", Started));
    }

    [Fact]
    public void An_explicit_owner_establishes_the_site_under_the_verified_DID()
    {
        var options = new SiteOptions { Name = " Our site ", OwnerDid = Owner };
        var site = TangentSite.Establish(options, Owner, Started);
        Assert.Equal("Our site", site.Name);
        Assert.Equal(Owner, site.OwnerDid);
        Assert.True(site.IsOwner(Owner));
        Assert.False(site.IsOwner(Visitor));
        Assert.False(site.IsOwner(null));
        Assert.Equal(1, site.PolicyRevision);
    }

    [Fact]
    public void Editing_configuration_cannot_transfer_persisted_ownership()
    {
        var site = TangentSite.Establish(new SiteOptions { OwnerDid = Owner }, Owner, Started);
        var error = Assert.Throws<InvalidOperationException>(() => site.CheckConfiguredOwner(new SiteOptions { OwnerDid = Visitor }));
        Assert.Contains("Tangent:Site:OwnerDid", error.Message);
        Assert.Equal(Owner, site.OwnerDid);
    }

    [Fact]
    public void A_handle_change_preserves_participant_identity_and_join_time()
    {
        var participant = Participant.FirstArrival(Owner, "old.example", Started);
        participant.Return(Owner, "new.example", Started.AddDays(1));
        Assert.Equal(Owner, participant.Id);
        Assert.Equal(Started, participant.JoinedAt);
        Assert.Equal("new.example", participant.Handle);
        Assert.Equal(Started.AddDays(1), participant.LastArrivedAt);
    }

    [Fact]
    public void Returning_with_a_changed_handle_preserves_site_suspension()
    {
        var participant = Participant.FirstArrival(Visitor, "before.example", Started);
        participant.IsSuspended = true;
        participant.Return(Visitor, "after.example", Started.AddMinutes(1));
        Assert.True(participant.IsSuspended);
        Assert.Equal(Visitor, participant.Id);
        Assert.Equal("after.example", participant.Handle);
        Assert.Equal(Started, participant.JoinedAt);
    }

    [Fact]
    public void Reusing_a_handle_does_not_inherit_another_DIDs_participant()
    {
        var original = Participant.FirstArrival(Owner, "shared.example", Started);
        Assert.Throws<InvalidOperationException>(() => original.Return(Visitor, "shared.example", Started.AddDays(1)));
        var newcomer = Participant.FirstArrival(Visitor, "shared.example", Started.AddDays(1));
        Assert.NotEqual(original.Id, newcomer.Id);
        Assert.Equal(Owner, original.Id);
        Assert.Equal(Started, original.LastArrivedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("alice.example")]
    [InlineData("not an identity")]
    public void A_handle_or_arbitrary_string_is_not_a_participant_key(string invalidDid)
        => Assert.ThrowsAny<ArgumentException>(() => Participant.FirstArrival(invalidDid, null, Started));
}
