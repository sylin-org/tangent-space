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

    [Fact]
    public void Creation_requires_the_persisted_site_owner_and_starts_pending()
    {
        var site = Site();
        Assert.Equal(RoomDenial.Forbidden, Assert.Throws<RoomRuleViolation>(() => Room.Create(site, Agent, "lounge", "Lounge", RoomAdmission.SignedIn, Now)).Denial);
        Assert.Throws<RoomRuleViolation>(() => Room.Create(null, Owner, "lounge", "Lounge", RoomAdmission.SignedIn, Now));
        var room = Room.Create(site, Owner, "lounge", " Lounge ", RoomAdmission.SignedIn, Now);
        Assert.Equal("lounge", room.Id);
        Assert.Equal("Lounge", room.Title);
        Assert.Equal(Owner, room.CreatorParticipantId);
        Assert.Equal(1, room.PolicyRevision);
        Assert.Equal(RoomSpaceState.Pending, room.SpaceState);
        Assert.Null(room.SpaceUri);
        var policy = room.CurrentPolicy(site, Owner, null);
        Assert.False(policy.CanRead);
        Assert.False(policy.CanWrite);
        Assert.True(policy.CanManage);
        Assert.Equal("space-pending", policy.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Workshop")]
    [InlineData("../workshop")]
    [InlineData("-workshop")]
    [InlineData("workshop-")]
    public void Room_keys_are_stable_canonical_local_identifiers(string key)
        => Assert.Equal(RoomDenial.InvalidInput,
            Assert.Throws<RoomRuleViolation>(() => Room.Create(Site(), Owner, key, "Room", RoomAdmission.SignedIn, Now)).Denial);

    [Fact]
    public void Mapping_requires_current_revision_and_retries_cannot_rebind_a_ready_room()
    {
        var site = Site();
        var room = Room.Create(site, Owner, "workshop", "Workshop", RoomAdmission.InvitationOnly, Now);
        Assert.Equal(RoomDenial.PolicyChanged, Assert.Throws<RoomRuleViolation>(() => room.CompleteSpace(site, Owner, 0, Space("workshop"), Now)).Denial);
        Assert.Null(room.SpaceUri);
        room.CompleteSpace(site, Owner, 1, Space("workshop"), Now);
        Assert.Equal(RoomSpaceState.Ready, room.SpaceState);
        Assert.Equal(2, room.PolicyRevision);
        room.CompleteSpace(site, Owner, 1, Space("workshop"), Now.AddMinutes(1));
        Assert.Equal(2, room.PolicyRevision);
        Assert.Equal(RoomDenial.SpaceAlreadyMapped, Assert.Throws<RoomRuleViolation>(() => room.CompleteSpace(site, Owner, 2, Space("other"), Now)).Denial);
        Assert.Equal(Space("workshop"), room.SpaceUri);
    }

    [Fact]
    public void Signed_in_admission_does_not_override_an_explicit_reader_or_removal()
    {
        var site = Site();
        var room = Ready(site, "lounge", RoomAdmission.SignedIn);
        Assert.True(room.CurrentPolicy(site, Agent, null).CanWrite);
        Assert.False(room.CurrentPolicy(site, null, null).CanRead);
        var reader = room.ChangeMembership(site, Owner, null, Agent, null, RoomRole.Reader, Now);
        var readOnly = room.CurrentPolicy(site, Agent, reader);
        Assert.True(readOnly.CanRead);
        Assert.False(readOnly.CanWrite);
        var removed = room.ChangeMembership(site, Owner, null, Agent, reader, RoomRole.Removed, Now);
        var denied = room.CurrentPolicy(site, Agent, removed);
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
        var room = Ready(site, "workshop", RoomAdmission.InvitationOnly);
        var manager = room.ChangeMembership(site, Owner, null, Manager, null, RoomRole.Manager, Now);
        Assert.False(room.CurrentPolicy(site, Agent, null).CanRead);
        var member = room.ChangeMembership(site, Manager, manager, Agent, null, RoomRole.Member, Now);
        Assert.True(room.CurrentPolicy(site, Agent, member).CanWrite);
        room.ChangeTopic(site, Manager, manager, " Cross-harness identity ", Now);
        Assert.Equal("Cross-harness identity", room.Topic);
        var reader = room.ChangeMembership(site, Manager, manager, Agent, member, RoomRole.Reader, Now);
        Assert.True(room.CurrentPolicy(site, Agent, reader).CanRead);
        Assert.False(room.CurrentPolicy(site, Agent, reader).CanWrite);
        var removed = room.ChangeMembership(site, Manager, manager, Agent, reader, RoomRole.Removed, Now);
        Assert.False(room.CurrentPolicy(site, Agent, removed).CanRead);
        Assert.Equal(Manager, removed.ChangedByParticipantId);
        Assert.Equal(room.PolicyRevision, removed.PolicyRevision);
    }

    [Fact]
    public void Manager_cannot_appoint_or_demote_other_managers_remove_owner_or_change_admission()
    {
        var site = Site();
        var room = Ready(site, "workshop", RoomAdmission.InvitationOnly);
        var manager = room.ChangeMembership(site, Owner, null, Manager, null, RoomRole.Manager, Now);
        var other = room.ChangeMembership(site, Owner, null, OtherManager, null, RoomRole.Manager, Now);
        var revision = room.PolicyRevision;
        Assert.Throws<RoomRuleViolation>(() => room.ChangeMembership(site, Manager, manager, Agent, null, RoomRole.Manager, Now));
        Assert.Throws<RoomRuleViolation>(() => room.ChangeMembership(site, Manager, manager, OtherManager, other, RoomRole.Removed, Now));
        Assert.Throws<RoomRuleViolation>(() => room.ChangeMembership(site, Manager, manager, Owner, null, RoomRole.Reader, Now));
        Assert.Throws<RoomRuleViolation>(() => room.ChangeAdmission(site, Manager, RoomAdmission.SignedIn, Now));
        Assert.Equal(revision, room.PolicyRevision);
        Assert.Equal(RoomRole.Manager, other.Role);
        Assert.Equal(Owner, room.CreatorParticipantId);
        Assert.Equal(RoomAdmission.InvitationOnly, room.Admission);
    }

    [Fact]
    public void Demotion_changes_the_next_policy_decision_without_any_cookie_role_input()
    {
        var site = Site();
        var room = Ready(site, "workshop", RoomAdmission.InvitationOnly);
        var manager = room.ChangeMembership(site, Owner, null, Manager, null, RoomRole.Manager, Now);
        Assert.True(room.CurrentPolicy(site, Manager, manager).CanManage);
        var member = room.ChangeMembership(site, Owner, null, Manager, manager, RoomRole.Member, Now);
        var current = room.CurrentPolicy(site, Manager, member);
        Assert.False(current.CanManage);
        Assert.True(current.CanWrite);
        Assert.Throws<RoomRuleViolation>(() => room.ChangeTopic(site, Manager, member, "Stale authority", Now));
        Assert.Empty(room.Topic);
        Assert.Equal(room.PolicyRevision, current.SelectedPolicyRevision);
        Assert.Equal(site.PolicyRevision, current.SitePolicyRevision);
    }

    [Fact]
    public void Manager_grants_cannot_cross_room_or_participant_boundaries()
    {
        var site = Site();
        var workshop = Ready(site, "workshop", RoomAdmission.InvitationOnly);
        var lounge = Ready(site, "lounge", RoomAdmission.SignedIn);
        var manager = workshop.ChangeMembership(site, Owner, null, Manager, null, RoomRole.Manager, Now);
        Assert.Equal(RoomDenial.MembershipMismatch, Assert.Throws<RoomRuleViolation>(() => lounge.CurrentPolicy(site, Manager, manager)).Denial);
        Assert.Throws<RoomRuleViolation>(() => workshop.CurrentPolicy(site, Agent, manager));
        Assert.NotEqual(RoomMembership.Key("workshop", Manager), RoomMembership.Key("lounge", Manager));
        Assert.NotEqual(RoomMembership.Key("workshop", Manager), RoomMembership.Key("workshop", Agent));
    }

    [Fact]
    public void A_manager_can_relinquish_own_authority_but_not_promote_it_again()
    {
        var site = Site();
        var room = Ready(site, "workshop", RoomAdmission.InvitationOnly);
        var manager = room.ChangeMembership(site, Owner, null, Manager, null, RoomRole.Manager, Now);
        var member = room.ChangeMembership(site, Manager, manager, Manager, manager, RoomRole.Member, Now);
        Assert.False(room.CurrentPolicy(site, Manager, member).CanManage);
        Assert.Throws<RoomRuleViolation>(() => room.ChangeMembership(site, Manager, member, Manager, member, RoomRole.Manager, Now));
    }

    [Fact]
    public void Suspension_denies_content_and_management_without_erasing_membership()
    {
        var site = Site();
        var room = Ready(site, "workshop", RoomAdmission.InvitationOnly);
        var manager = room.ChangeMembership(site, Owner, null, Manager, null, RoomRole.Manager, Now);
        var denied = room.CurrentPolicy(site, Manager, manager, suspended: true);
        Assert.False(denied.CanRead);
        Assert.False(denied.CanWrite);
        Assert.False(denied.CanManage);
        Assert.Equal("suspended", denied.Reason);
        Assert.Equal(room.PolicyRevision, denied.SelectedPolicyRevision);
        Assert.Equal(RoomRole.Manager, manager.Role);
        var restored = room.CurrentPolicy(site, Manager, manager, suspended: false);
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
        { new CreateRoomRequest("workshop", "Workshop", RoomAdmission.InvitationOnly),
            """{"key":"workshop","title":"Workshop","admission":"InvitationOnly"}""" },
        { new ChangeRoomMembershipRequest(RoomRole.Reader), """{"role":"Reader"}""" },
        { new ChangeRoomTopicRequest("Shared conversation"), """{"topic":"Shared conversation"}""" },
        { new ChangeRoomAdmissionRequest(RoomAdmission.SignedIn), """{"admission":"SignedIn"}""" },
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

    private static Room Ready(TangentSite site, string key, RoomAdmission admission)
    {
        var room = Room.Create(site, Owner, key, key, admission, Now);
        room.CompleteSpace(site, Owner, room.PolicyRevision, Space(key), Now);
        return room;
    }

    private static string Space(string key) => $"at://{Owner}/chat.tangent.space/{key}";
}
