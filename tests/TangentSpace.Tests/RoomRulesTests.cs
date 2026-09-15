using TangentSpace.Communities;
using TangentSpace.Rooms;
using TangentSpace.Rooms.Web;
using TangentSpace.Site;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace TangentSpace.Tests;

public sealed class RoomRulesTests
{
    private const string OwnerDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly string Owner = TangentSpace.Participants.Participant.NewIdentifier();
    private static readonly string Manager = TangentSpace.Participants.Participant.NewIdentifier();
    private static readonly string Agent = TangentSpace.Participants.Participant.NewIdentifier();
    private static readonly string OtherManager = TangentSpace.Participants.Participant.NewIdentifier();
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static TangentSite Site() => TangentSite.Establish(new SiteOptions { Name = "Tangent", OwnerDid = OwnerDid }, OwnerDid, Owner, Now);
    private static TangentCommunity Home(TangentSite site) => TangentCommunity.Home(site);

    [Fact]
    public void Creation_requires_the_Tangent_owner_and_is_ready_at_once()
    {
        var site = Site();
        var home = Home(site);
        Assert.Equal(RoomDenial.Forbidden, Assert.Throws<RoomRuleViolation>(() => Room.Create(site, Agent, "lounge", "Lounge", RoomAdmission.SignedIn, Now, home)).Denial);
        Assert.Throws<RoomRuleViolation>(() => Room.Create(null, Owner, "lounge", "Lounge", RoomAdmission.SignedIn, Now, home));
        var room = Room.Create(site, Owner, "lounge", " Lounge ", RoomAdmission.SignedIn, Now, home);
        Assert.Equal("lounge", room.Id);
        Assert.Equal(home.Id, room.TangentKey);
        Assert.Equal("Lounge", room.Title);
        Assert.Equal(Owner, room.CreatorParticipantId);
        Assert.Equal(1, room.PolicyRevision);
        var policy = room.CurrentPolicy(site, Owner, null, tangent: home);
        Assert.True(policy.CanRead);
        Assert.True(policy.CanWrite);
        Assert.True(policy.CanManage);
        Assert.Equal("allowed", policy.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Workshop")]
    [InlineData("../workshop")]
    [InlineData("-workshop")]
    [InlineData("workshop-")]
    public void Room_keys_are_stable_canonical_local_identifiers(string key)
    {
        var site = Site();
        Assert.Equal(RoomDenial.InvalidInput,
            Assert.Throws<RoomRuleViolation>(() => Room.Create(site, Owner, key, "Room", RoomAdmission.SignedIn, Now, Home(site))).Denial);
    }

    [Fact]
    public void Public_reading_is_an_explicit_owner_change_that_acknowledges_existing_history()
    {
        var site = Site();
        var room = Topic(site, Home(site), "salon", RoomAdmission.InvitationOnly);
        Assert.Equal(RoomReadAudience.Restricted, room.ReadAudience);
        var revision = room.PolicyRevision;

        var missingAcknowledgement = Assert.Throws<RoomRuleViolation>(() => room.ChangeReadAudience(
            site, Owner, RoomReadAudience.Public, publishExistingHistory: false, Now.AddMinutes(1)));
        Assert.Equal(RoomDenial.InvalidInput, missingAcknowledgement.Denial);
        Assert.Equal(RoomReadAudience.Restricted, room.ReadAudience);
        Assert.Equal(revision, room.PolicyRevision);

        room.ChangeReadAudience(site, Owner, RoomReadAudience.Public, publishExistingHistory: true, Now.AddMinutes(1));
        Assert.Equal(RoomReadAudience.Public, room.ReadAudience);
        Assert.Equal(revision + 1, room.PolicyRevision);

        // Narrowing the audience does not need a publication acknowledgement.
        room.ChangeReadAudience(site, Owner, RoomReadAudience.Restricted, publishExistingHistory: false, Now.AddMinutes(2));
        Assert.Equal(RoomReadAudience.Restricted, room.ReadAudience);
    }

    [Fact]
    public void Host_or_tangent_owner_can_publish_but_a_topic_manager_cannot()
    {
        var site = Site();
        var tangentOwner = Agent;
        var tangent = new TangentCommunity
        {
            Id = "salon", OwnerParticipantId = tangentOwner, Name = "Salon"
        };
        var room = Room.CreateDelegated(site, Manager, "salon-talk", "Talk", RoomAdmission.SignedIn, Now, tangent);

        Assert.Equal(RoomDenial.Forbidden, Assert.Throws<RoomRuleViolation>(() => room.ChangeReadAudience(
            site, Manager, RoomReadAudience.Public, true, Now.AddMinutes(1), tangent)).Denial);
        room.ChangeReadAudience(site, tangentOwner, RoomReadAudience.Public, true, Now.AddMinutes(1), tangent);
        room.ChangeReadAudience(site, Owner, RoomReadAudience.Restricted, false, Now.AddMinutes(2), tangent);
        Assert.Equal(RoomReadAudience.Restricted, room.ReadAudience);
    }

    [Fact]
    public void Signed_in_admission_does_not_override_an_explicit_reader_or_removal()
    {
        var site = Site();
        var home = Home(site);
        var room = Topic(site, home, "lounge", RoomAdmission.SignedIn);
        Assert.True(room.CurrentPolicy(site, Agent, null, tangent: home).CanWrite);
        Assert.False(room.CurrentPolicy(site, null, null, tangent: home).CanRead);
        var reader = room.ChangeMembership(site, Owner, null, Agent, null, RoomRole.Reader, Now, home);
        var readOnly = room.CurrentPolicy(site, Agent, reader, tangent: home);
        Assert.True(readOnly.CanRead);
        Assert.False(readOnly.CanWrite);
        var removed = room.ChangeMembership(site, Owner, null, Agent, reader, RoomRole.Removed, Now, home);
        var denied = room.CurrentPolicy(site, Agent, removed, tangent: home);
        Assert.False(denied.CanRead);
        Assert.False(denied.CanWrite);
        Assert.False(denied.CanManage);
        Assert.Equal("removed", denied.Reason);
        Assert.Equal(room.PolicyRevision, denied.SelectedPolicyRevision);
    }

    [Fact]
    public void Manager_can_admit_restrict_remove_and_change_topic_for_ordinary_members()
    {
        var site = Site();
        var home = Home(site);
        var room = Topic(site, home, "workshop", RoomAdmission.InvitationOnly);
        var manager = room.ChangeMembership(site, Owner, null, Manager, null, RoomRole.Manager, Now, home);
        Assert.False(room.CurrentPolicy(site, Agent, null, tangent: home).CanRead);
        var member = room.ChangeMembership(site, Manager, manager, Agent, null, RoomRole.Member, Now, home);
        Assert.True(room.CurrentPolicy(site, Agent, member, tangent: home).CanWrite);
        room.ChangeTopic(site, Manager, manager, " Cross-harness identity ", Now, home);
        Assert.Equal("Cross-harness identity", room.Topic);
        var reader = room.ChangeMembership(site, Manager, manager, Agent, member, RoomRole.Reader, Now, home);
        Assert.True(room.CurrentPolicy(site, Agent, reader, tangent: home).CanRead);
        Assert.False(room.CurrentPolicy(site, Agent, reader, tangent: home).CanWrite);
        var removed = room.ChangeMembership(site, Manager, manager, Agent, reader, RoomRole.Removed, Now, home);
        Assert.False(room.CurrentPolicy(site, Agent, removed, tangent: home).CanRead);
        Assert.Equal(Manager, removed.ChangedByParticipantId);
        Assert.Equal(room.PolicyRevision, removed.PolicyRevision);
    }

    [Fact]
    public void Manager_cannot_appoint_or_demote_other_managers_remove_owner_or_change_admission()
    {
        var site = Site();
        var home = Home(site);
        var room = Topic(site, home, "workshop", RoomAdmission.InvitationOnly);
        var manager = room.ChangeMembership(site, Owner, null, Manager, null, RoomRole.Manager, Now, home);
        var other = room.ChangeMembership(site, Owner, null, OtherManager, null, RoomRole.Manager, Now, home);
        var revision = room.PolicyRevision;
        Assert.Throws<RoomRuleViolation>(() => room.ChangeMembership(site, Manager, manager, Agent, null, RoomRole.Manager, Now, home));
        Assert.Throws<RoomRuleViolation>(() => room.ChangeMembership(site, Manager, manager, OtherManager, other, RoomRole.Removed, Now, home));
        Assert.Throws<RoomRuleViolation>(() => room.ChangeMembership(site, Manager, manager, Owner, null, RoomRole.Reader, Now, home));
        Assert.Throws<RoomRuleViolation>(() => room.ChangeAdmission(site, Manager, RoomAdmission.SignedIn, Now, home));
        Assert.Equal(revision, room.PolicyRevision);
        Assert.Equal(RoomRole.Manager, other.Role);
        Assert.Equal(Owner, room.CreatorParticipantId);
        Assert.Equal(RoomAdmission.InvitationOnly, room.Admission);
    }

    [Fact]
    public void Demotion_changes_the_next_policy_decision_without_any_cookie_role_input()
    {
        var site = Site();
        var home = Home(site);
        var room = Topic(site, home, "workshop", RoomAdmission.InvitationOnly);
        var manager = room.ChangeMembership(site, Owner, null, Manager, null, RoomRole.Manager, Now, home);
        Assert.True(room.CurrentPolicy(site, Manager, manager, tangent: home).CanManage);
        var member = room.ChangeMembership(site, Owner, null, Manager, manager, RoomRole.Member, Now, home);
        var current = room.CurrentPolicy(site, Manager, member, tangent: home);
        Assert.False(current.CanManage);
        Assert.True(current.CanWrite);
        Assert.Throws<RoomRuleViolation>(() => room.ChangeTopic(site, Manager, member, "Stale authority", Now, home));
        Assert.Empty(room.Topic);
        Assert.Equal(room.PolicyRevision, current.SelectedPolicyRevision);
        Assert.Equal(site.PolicyRevision, current.SitePolicyRevision);
    }

    [Fact]
    public void Manager_grants_cannot_cross_room_or_participant_boundaries()
    {
        var site = Site();
        var home = Home(site);
        var workshop = Topic(site, home, "workshop", RoomAdmission.InvitationOnly);
        var lounge = Topic(site, home, "lounge", RoomAdmission.SignedIn);
        var manager = workshop.ChangeMembership(site, Owner, null, Manager, null, RoomRole.Manager, Now, home);
        Assert.Equal(RoomDenial.MembershipMismatch, Assert.Throws<RoomRuleViolation>(() => lounge.CurrentPolicy(site, Manager, manager, tangent: home)).Denial);
        Assert.Throws<RoomRuleViolation>(() => workshop.CurrentPolicy(site, Agent, manager, tangent: home));
        Assert.NotEqual(RoomMembership.Key("workshop", Manager), RoomMembership.Key("lounge", Manager));
        Assert.NotEqual(RoomMembership.Key("workshop", Manager), RoomMembership.Key("workshop", Agent));
    }

    [Fact]
    public void A_manager_can_relinquish_own_authority_but_not_promote_it_again()
    {
        var site = Site();
        var home = Home(site);
        var room = Topic(site, home, "workshop", RoomAdmission.InvitationOnly);
        var manager = room.ChangeMembership(site, Owner, null, Manager, null, RoomRole.Manager, Now, home);
        var member = room.ChangeMembership(site, Manager, manager, Manager, manager, RoomRole.Member, Now, home);
        Assert.False(room.CurrentPolicy(site, Manager, member, tangent: home).CanManage);
        Assert.Throws<RoomRuleViolation>(() => room.ChangeMembership(site, Manager, member, Manager, member, RoomRole.Manager, Now, home));
    }

    [Fact]
    public void Suspension_denies_content_and_management_without_erasing_membership()
    {
        var site = Site();
        var home = Home(site);
        var room = Topic(site, home, "workshop", RoomAdmission.InvitationOnly);
        var manager = room.ChangeMembership(site, Owner, null, Manager, null, RoomRole.Manager, Now, home);
        var denied = room.CurrentPolicy(site, Manager, manager, suspended: true, tangent: home);
        Assert.False(denied.CanRead);
        Assert.False(denied.CanWrite);
        Assert.False(denied.CanManage);
        Assert.Equal("suspended", denied.Reason);
        Assert.Equal(room.PolicyRevision, denied.SelectedPolicyRevision);
        Assert.Equal(RoomRole.Manager, manager.Role);
        var restored = room.CurrentPolicy(site, Manager, manager, suspended: false, tangent: home);
        Assert.True(restored.CanRead);
        Assert.True(restored.CanWrite);
        Assert.True(restored.CanManage);
    }

    [Theory]
    [InlineData("https://tangent.example", "https", "tangent.example", true)]
    [InlineData("https://tangent.example:443", "https", "tangent.example", true)]
    [InlineData("https://TANGENT.example", "https", "tangent.example", true)]
    [InlineData("http://tangent.example", "https", "tangent.example", false)]
    [InlineData("https://attacker.example", "https", "tangent.example", false)]
    [InlineData("https://tangent.example:8443", "https", "tangent.example", false)]
    [InlineData("https://tangent.example/path", "https", "tangent.example", false)]
    [InlineData("https://user@tangent.example", "https", "tangent.example", false)]
    [InlineData("null", "https", "tangent.example", false)]
    [InlineData(null, "https", "tangent.example", false)]
    public void Administrative_origin_matches_scheme_host_and_port(string? origin, string scheme, string host, bool accepted)
        => Assert.Equal(accepted, RoomMutationAttribute.IsSameOrigin(origin, scheme, host));

    public static TheoryData<object, string> AdministrativeBodies => new()
    {
        { new ChangeRoomMembershipRequest(RoomRole.Reader), """{"role":"Reader"}""" },
        { new ChangeRoomTopicRequest("Shared conversation"), """{"topic":"Shared conversation"}""" },
        { new ChangeRoomAdmissionRequest(RoomAdmission.SignedIn), """{"admission":"SignedIn"}""" },
        { new ChangeRoomReadAudienceRequest(RoomReadAudience.Public, true),
            """{"audience":"Public","publishExistingHistory":true}""" },
        { new ChangeSuspensionRequest(true), """{"suspended":true}""" }
    };

    [Theory]
    [MemberData(nameof(AdministrativeBodies))]
    public void Administrative_requests_use_Newtonsoft_and_require_exact_explicit_fields(object expected, string json)
    {
        // Koan.Web MVC uses Newtonsoft with camel-case properties and string enums.
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter() }
        };
        Assert.Equal(expected, JsonConvert.DeserializeObject(json, expected.GetType(), settings));
        var withUnrecognizedAuthority = JObject.Parse(json);
        withUnrecognizedAuthority["ownerDid"] = Owner;
        Assert.Throws<JsonSerializationException>(() =>
            JsonConvert.DeserializeObject(withUnrecognizedAuthority.ToString(), expected.GetType(), settings));
        foreach (var property in JObject.Parse(json).Properties())
        {
            var omitted = JObject.Parse(json);
            omitted.Remove(property.Name);
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject(omitted.ToString(), expected.GetType(), settings));
            var nullValue = JObject.Parse(json);
            nullValue[property.Name] = JValue.CreateNull();
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject(nullValue.ToString(), expected.GetType(), settings));
        }
    }

    private static Room Topic(TangentSite site, TangentCommunity home, string key, RoomAdmission admission)
        => Room.Create(site, Owner, key, key, admission, Now, home);
}
