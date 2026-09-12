using TangentSpace.Participants;
using TangentSpace.Site;
using Xunit;

namespace TangentSpace.Tests;

public sealed class ArrivalRulesTests
{
    private const string OwnerDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string VisitorDid = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly DateTimeOffset Started = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Owner = Participant.NewIdentifier();
    private static readonly string Visitor = Participant.NewIdentifier();

    [Fact]
    public void A_first_visitor_cannot_claim_ownership()
    {
        var options = new SiteOptions { Name = "Our site", OwnerDid = OwnerDid };
        Assert.Throws<InvalidOperationException>(() => TangentSite.Establish(options, VisitorDid, Visitor, Started));
    }

    [Fact]
    public async Task An_unconfigured_site_can_be_claimed_by_a_verified_account()
    {
        var site = TangentSite.Establish(new SiteOptions(), VisitorDid, Visitor, Started);
        Assert.Equal(Visitor, site.OwnerParticipantId);
        await site.CheckConfiguredOwner(new SiteOptions());
        Assert.Throws<ArgumentException>(() => TangentSite.Establish(new SiteOptions(), "visitor.test", Visitor, Started));
    }

    [Fact]
    public void An_explicit_owner_establishes_the_site_under_the_verified_DID()
    {
        var options = new SiteOptions { Name = " Our site ", OwnerDid = OwnerDid };
        var site = TangentSite.Establish(options, OwnerDid, Owner, Started);
        Assert.Equal("Our site", site.Name);
        Assert.Equal(Owner, site.OwnerParticipantId);
        Assert.True(site.IsOwner(Owner));
        Assert.False(site.IsOwner(Visitor));
        Assert.False(site.IsOwner(null));
        Assert.Equal(1, site.PolicyRevision);
    }

    [Fact]
    public async Task A_blank_configuration_never_conflicts_with_a_persisted_owner()
    {
        var site = TangentSite.Establish(new SiteOptions { OwnerDid = OwnerDid }, OwnerDid, Owner, Started);
        await site.CheckConfiguredOwner(new SiteOptions());
        Assert.Equal(Owner, site.OwnerParticipantId);
    }

    [Fact]
    public void First_arrival_mints_the_spine_and_both_initial_identities()
    {
        var (participant, identities) = Participant.Enroll(OwnerDid, "old.example", Started);
        Assert.NotEqual(OwnerDid, participant.Id);
        Assert.True(Participant.IsValidId(participant.Id));
        Assert.Equal(Started, participant.JoinedAt);
        Assert.Equal(Started, participant.LastArrivedAt);
        Assert.Equal(2, identities.Count);
        var atproto = identities.Single(identity => identity.Kind == ParticipantIdentity.AtprotoKind);
        Assert.Equal(OwnerDid, atproto.Value);
        Assert.Equal("old.example", atproto.Label);
        Assert.Equal(participant.Id, atproto.ParticipantId);
        var internalIdentity = identities.Single(identity => identity.Kind == ParticipantIdentity.InternalKind);
        Assert.Equal("tangent:local:" + participant.Id, internalIdentity.Value);
        Assert.Null(internalIdentity.Label);
    }

    [Fact]
    public void A_return_refreshes_arrival_time_and_the_atproto_label()
    {
        var (participant, identities) = Participant.Enroll(OwnerDid, "old.example", Started);
        var atproto = identities.Single(identity => identity.Kind == ParticipantIdentity.AtprotoKind);
        participant.Return(atproto, Started.AddDays(1));
        atproto.Relabel("new.example");
        Assert.Equal(Started, participant.JoinedAt);
        Assert.Equal(Started.AddDays(1), participant.LastArrivedAt);
        Assert.Equal("new.example", atproto.Label);
        Assert.Equal(OwnerDid, atproto.Value);
    }

    [Fact]
    public void A_return_rejects_an_identity_owned_by_another_participant()
    {
        var (participant, identities) = Participant.Enroll(OwnerDid, "old.example", Started);
        var (other, otherIdentities) = Participant.Enroll(VisitorDid, "other.example", Started);
        var foreignAtproto = otherIdentities.Single(identity => identity.Kind == ParticipantIdentity.AtprotoKind);
        Assert.Throws<InvalidOperationException>(() => participant.Return(foreignAtproto, Started.AddDays(1)));
        Assert.Throws<InvalidOperationException>(() => participant.Return(
            identities.Single(identity => identity.Kind == ParticipantIdentity.InternalKind), Started.AddDays(1)));
        Assert.Equal(Started, participant.LastArrivedAt);
        Assert.NotEqual(participant.Id, other.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("alice.example")]
    [InlineData("not an identity")]
    public void A_handle_or_arbitrary_string_is_not_an_atproto_identity(string invalidDid)
        => Assert.ThrowsAny<ArgumentException>(() => Participant.Enroll(invalidDid, null, Started));
}
