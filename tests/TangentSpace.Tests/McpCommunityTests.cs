using TangentSpace.Communities;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using TangentSpace.Site;
using Xunit;

namespace TangentSpace.Tests;

/// <summary>Rule tests for companion participation: self join/remove/leave, invitations, role scope, restrictions.</summary>
public sealed class McpCommunityTests
{
    private const string Owner = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Member = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Agent = "did:plc:cccccccccccccccccccccccc";
    private const string Other = "did:plc:dddddddddddddddddddddddd";
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static TangentSite Site() => TangentSite.Establish(new SiteOptions { Name = "Host", OwnerDid = Owner }, Owner, Now);

    private static TangentCommunity ClosedTangent() => TangentCommunity.Create(Site(), Owner, "kintsugi", "Kintsugi", "", "", "", "", Now);

    private static TangentCommunity OpenTangent()
    {
        var tangent = ClosedTangent();
        tangent.OpenToSignedIn = true;
        return tangent;
    }

    private static TangentCommunity ApprovalTangent()
    {
        var tangent = ClosedTangent();
        tangent.ApprovalRequired = true;
        return tangent;
    }

    private static TangentMembership Role(TangentCommunity tangent, string did, TangentRole role)
        => TangentMembership.Assign(tangent, did, role, Owner, Now);

    [Fact]
    public void Self_join_admission_follows_the_effective_tangent_admission()
    {
        var open = OpenTangent();
        Assert.Equal(TangentAdmission.Open, open.EffectiveAdmission);
        Assert.True(open.CanParticipate(Agent, null));
        Assert.True(open.CanParticipate(Agent, Role(open, Agent, TangentRole.Left)));

        var closed = ClosedTangent();
        Assert.Equal(TangentAdmission.Invite, closed.EffectiveAdmission);
        Assert.False(closed.CanParticipate(Agent, null));
        Assert.False(closed.CanParticipate(Agent, Role(closed, Agent, TangentRole.Left)));

        var approval = ApprovalTangent();
        Assert.Equal(TangentAdmission.Approval, approval.EffectiveAdmission);
        // Approval is visible for a request but grants nothing on its own.
        Assert.False(approval.CanParticipate(Agent, null));
    }

    [Fact]
    public void A_durable_admin_removal_overrides_every_self_join_path()
    {
        var open = OpenTangent();
        var removed = Role(open, Agent, TangentRole.Removed);
        Assert.False(open.CanParticipate(Agent, removed));
        // Voluntary departure stays distinct: an open community still admits a returned participant.
        Assert.True(open.CanParticipate(Agent, Role(open, Agent, TangentRole.Left)));
        // Community roles participate; the owner always does.
        Assert.True(open.CanParticipate(Agent, Role(open, Agent, TangentRole.Admin)));
        Assert.True(open.CanParticipate(Agent, Role(open, Agent, TangentRole.Reader)));
        Assert.True(open.CanParticipate(Owner, null));
    }

    [Fact]
    public void Invitations_bind_to_recipient_and_tangent_and_expire_revoke_and_redeem_once()
    {
        var tangent = ClosedTangent();
        var invitation = TangentInvitation.Issue(tangent, Agent, TangentRole.Reader, Owner, Now);
        Assert.Equal(tangent.Id, invitation.TangentKey);
        Assert.Equal(Agent, invitation.RecipientDid);
        Assert.True(invitation.Usable(Now));
        Assert.True(invitation.Usable(invitation.ExpiresAt.AddSeconds(-1)));

        invitation.Redeem(Agent, Now.AddMinutes(1));
        Assert.False(invitation.Usable(Now.AddMinutes(2)));
        Assert.Equal(TangentDenial.InvalidInput,
            Assert.Throws<TangentRuleViolation>(() => invitation.Redeem(Agent, Now.AddMinutes(2))).Denial);

        var revoked = TangentInvitation.Issue(tangent, Agent, TangentRole.Member, Owner, Now);
        revoked.Revoke(Owner, Now.AddMinutes(1));
        Assert.False(revoked.Usable(Now.AddMinutes(2)));
        Assert.Equal(TangentDenial.Forbidden,
            Assert.Throws<TangentRuleViolation>(() => revoked.Redeem(Agent, Now.AddMinutes(3))).Denial);

        var expired = TangentInvitation.Issue(tangent, Agent, TangentRole.Member, Owner, Now);
        Assert.Equal(TangentDenial.Forbidden,
            Assert.Throws<TangentRuleViolation>(() => expired.Redeem(Agent, expired.ExpiresAt.AddSeconds(1))).Denial);
        Assert.False(TangentInvitation.IsValidGrant(TangentRole.Removed));
        Assert.False(TangentInvitation.IsValidGrant(TangentRole.Left));
    }

    [Fact]
    public void A_pending_admission_request_is_durable_idempotent_and_decides_once()
    {
        var request = TangentJoinRequest.Open("kintsugi", Agent, Now);
        Assert.True(request.Pending);
        Assert.Equal(TangentJoinRequest.Key("kintsugi", Agent), request.Id);
        Assert.NotEqual(TangentJoinRequest.Key("kintsugi", Agent), TangentJoinRequest.Key("kintsugi", Member));

        request.Decide(Owner, true, Now.AddMinutes(5));
        Assert.False(request.Pending);
        Assert.True(request.Accepted);
        Assert.Equal(TangentDenial.InvalidInput,
            Assert.Throws<TangentRuleViolation>(() => request.Decide(Owner, false, Now.AddMinutes(6))).Denial);
    }

    [Fact]
    public void Restrictions_need_a_future_timeout_window_and_lift_only_their_own_record()
    {
        var timeout = ScopedRestriction.Impose(RestrictionScope.Room, "lounge", Agent, RestrictionKind.Timeout,
            Now.AddHours(1), "Cooling off.", Owner, Now);
        Assert.Equal(Now.AddHours(1), timeout.Until);
        Assert.NotNull(Restrictions.Evaluate(timeout, Now));
        // Expiry is evaluation-time: the same record stops restricting without any scheduler.
        Assert.Null(Restrictions.Evaluate(timeout, Now.AddHours(2)));

        var ban = ScopedRestriction.Impose(RestrictionScope.Tangent, "kintsugi", Agent, RestrictionKind.Ban,
            null, "Repeated misuse.", Owner, Now);
        Assert.True(Restrictions.Evaluate(ban, Now.AddYears(1))!.Banned);
        Assert.Null(ban.Until);

        var lifted = ScopedRestriction.Impose(RestrictionScope.Room, "lounge", Agent, RestrictionKind.None,
            null, "Resolved.", Owner, Now);
        Assert.Null(Restrictions.Evaluate(lifted, Now));

        Assert.Equal(RoomDenial.InvalidInput, Assert.Throws<RoomRuleViolation>(() =>
            ScopedRestriction.Impose(RestrictionScope.Room, "lounge", Agent, RestrictionKind.Timeout, null, "x", Owner, Now)).Denial);
        Assert.Equal(RoomDenial.InvalidInput, Assert.Throws<RoomRuleViolation>(() =>
            ScopedRestriction.Impose(RestrictionScope.Room, "lounge", Agent, RestrictionKind.Timeout, Now.AddHours(-1), "past", Owner, Now)).Denial);
        Assert.Equal(RoomDenial.InvalidInput, Assert.Throws<RoomRuleViolation>(() =>
            ScopedRestriction.Impose(RestrictionScope.Room, "lounge", Agent, RestrictionKind.Ban, Now.AddHours(1), "no expiry", Owner, Now)).Denial);
        // A scoped lift never evaluates as a restriction and keeps distinct keys per scope.
        Assert.NotEqual(ScopedRestriction.Key(RestrictionScope.Room, "lounge", Agent),
            ScopedRestriction.Key(RestrictionScope.Tangent, "lounge", Agent));
    }

    [Fact]
    public void Tangent_administrators_manage_channels_but_never_own_them()
    {
        var site = Site();
        var tangent = ClosedTangent();
        var adminMembership = Role(tangent, Member, TangentRole.Admin);
        var room = Room.CreateDelegated(site, Member, "kintsugi-work", "Workshop", RoomAdmission.SignedIn, Now, tangent.Id, tangent);
        room.CompleteSpace(site, Owner, room.PolicyRevision, Space("kintsugi-work"), Now, tangent);

        Assert.Equal(Member, room.CreatorOwnerDid);
        var adminPolicy = room.CurrentPolicy(site, Member, null, tangent: tangent, tangentMembership: adminMembership);
        Assert.True(adminPolicy.CanManage);
        Assert.True(adminPolicy.CanAppointManagers == false);
        Assert.False(adminPolicy.IsOwner);
        Assert.True(adminPolicy.CanRead);
        Assert.True(adminPolicy.CanWrite);
        // The owner still outranks the delegated creator inside the Tangent.
        Assert.True(room.CurrentPolicy(site, Owner, null, tangent: tangent).IsOwner);
        // A room removal overrides the delegated community authority.
        var removed = room.ChangeMembership(site, Owner, null, Member, null, RoomRole.Removed, Now, tangent, adminMembership);
        var denied = room.CurrentPolicy(site, Member, removed, tangent: tangent, tangentMembership: adminMembership);
        Assert.False(denied.CanManage);
        Assert.Equal("removed", denied.Reason);
    }

    [Fact]
    public void Tangent_readers_read_but_cannot_write_unless_a_channel_grant_widens_them()
    {
        var site = Site();
        var tangent = OpenTangent();
        var readerMembership = Role(tangent, Agent, TangentRole.Reader);
        var room = Room.Create(site, Owner, "open-lounge", "Lounge", RoomAdmission.SignedIn, Now, tangent.Id, tangent);
        room.CompleteSpace(site, Owner, room.PolicyRevision, Space("open-lounge"), Now, tangent);

        var reader = room.CurrentPolicy(site, Agent, null, tangent: tangent, tangentMembership: readerMembership);
        Assert.True(reader.CanRead);
        Assert.False(reader.CanWrite);

        var channelMember = room.ChangeMembership(site, Owner, null, Agent, null, RoomRole.Member, Now, tangent, readerMembership);
        var widened = room.CurrentPolicy(site, Agent, channelMember, tangent: tangent, tangentMembership: readerMembership);
        Assert.True(widened.CanWrite);
    }

    [Fact]
    public void Channel_role_mapping_keeps_manager_authority_within_the_room_guards()
    {
        var site = Site();
        var tangent = ClosedTangent();
        var room = Room.Create(site, Owner, "kintsugi-crew", "Crew", RoomAdmission.InvitationOnly, Now, tangent.Id, tangent);
        room.CompleteSpace(site, Owner, room.PolicyRevision, Space("kintsugi-crew"), Now, tangent);
        var manager = room.ChangeMembership(site, Owner, null, Member, null, RoomRole.Manager, Now, tangent);
        // A manager cannot appoint another manager and cannot touch the Tangent owner.
        Assert.Throws<RoomRuleViolation>(() =>
            room.ChangeMembership(site, Member, manager, Agent, null, RoomRole.Manager, Now, tangent, Role(tangent, Member, TangentRole.Member)));
        Assert.Throws<RoomRuleViolation>(() =>
            room.ChangeMembership(site, Member, manager, Owner, null, RoomRole.Reader, Now, tangent, Role(tangent, Member, TangentRole.Member)));
        // Ordinary grants stay within the manager's authority.
        var granted = room.ChangeMembership(site, Member, manager, Agent, null, RoomRole.Member, Now, tangent, Role(tangent, Member, TangentRole.Member));
        Assert.True(room.CurrentPolicy(site, Agent, granted, tangent: tangent, tangentMembership: Role(tangent, Agent, TangentRole.Member)).CanWrite);
    }

    [Fact]
    public void Owner_membership_changes_in_a_delegated_channel_do_not_treat_the_creator_as_ownership()
    {
        var site = Site();
        var tangent = ClosedTangent();
        var room = Room.CreateDelegated(site, Member, "kintsugi-agent", "Agent Lounge", RoomAdmission.InvitationOnly, Now, tangent.Id, tangent);
        room.CompleteSpace(site, Owner, room.PolicyRevision, Space("kintsugi-agent"), Now, tangent);
        // The Tangent owner manages the creator's standing; ownership protections still guard the owner.
        var demoted = room.ChangeMembership(site, Owner, null, Member, null, RoomRole.Reader, Now, tangent);
        Assert.True(room.CurrentPolicy(site, Member, demoted, tangent: tangent, tangentMembership: Role(tangent, Member, TangentRole.Member)).CanRead);
        Assert.Throws<RoomRuleViolation>(() =>
            room.ChangeMembership(site, Owner, null, Owner, null, RoomRole.Removed, Now, tangent));
    }

    [Fact]
    public void Tangent_removal_overrides_an_old_channel_manager_grant()
    {
        var site = Site();
        var tangent = OpenTangent();
        var room = Room.Create(site, Owner, "removed-manager", "Room", RoomAdmission.SignedIn, Now, tangent.Id, tangent);
        room.CompleteSpace(site, Owner, room.PolicyRevision, Space("removed-manager"), Now, tangent);
        var manager = room.ChangeMembership(site, Owner, null, Agent, null, RoomRole.Manager, Now, tangent);
        var removed = room.CurrentPolicy(site, Agent, manager, tangent: tangent,
            tangentMembership: Role(tangent, Agent, TangentRole.Removed));
        Assert.False(removed.CanRead);
        Assert.False(removed.CanWrite);
        Assert.False(removed.CanManage);
    }

    [Fact]
    public void Classification_presets_deny_read_and_write_from_declarations_only()
    {
        var site = Site();
        var tangent = ClosedTangent();
        tangent.ChangeParticipationPolicy(Owner, TangentAdmission.Invite, ParticipationPreset.HumansOnly, UndeclaredAccess.Deny, Now);
        var memberRoom = Room.Create(site, Owner, "members", "Members", RoomAdmission.SignedIn, Now, tangent.Id, tangent);
        memberRoom.CompleteSpace(site, Owner, memberRoom.PolicyRevision, Space("members"), Now, tangent);
        var membership = Role(tangent, Agent, TangentRole.Member);

        // Undeclared is never human: deny by default, per the stored undeclared access.
        var undeclared = memberRoom.CurrentPolicy(site, Agent, null, tangent: tangent, tangentMembership: membership,
            classification: ParticipantClassification.Undeclared);
        Assert.False(undeclared.CanRead);
        Assert.False(undeclared.CanWrite);
        Assert.Equal("participation-policy", undeclared.Reason);

        // An explicit human declaration participates; an explicit agent declaration does not.
        var human = memberRoom.CurrentPolicy(site, Agent, null, tangent: tangent, tangentMembership: membership,
            classification: ParticipantClassification.Human);
        Assert.True(human.CanRead);
        Assert.True(human.CanWrite);
        var agent = memberRoom.CurrentPolicy(site, Agent, null, tangent: tangent, tangentMembership: membership,
            classification: ParticipantClassification.Agent);
        Assert.False(agent.CanRead);
        Assert.Equal("participation-policy", agent.Reason);

        // The owner remains an authority regardless of classification.
        var ownerPolicy = memberRoom.CurrentPolicy(site, Owner, null, tangent: tangent,
            classification: ParticipantClassification.Undeclared);
        Assert.True(ownerPolicy.CanManage);
        Assert.True(ownerPolicy.CanRead);
    }

    [Theory]
    [InlineData(ParticipationPreset.HumansWriteAgentsRead, ParticipantClassification.Human, true, true)]
    [InlineData(ParticipationPreset.HumansWriteAgentsRead, ParticipantClassification.Agent, true, false)]
    [InlineData(ParticipationPreset.HumansReadAgentsWrite, ParticipantClassification.Human, true, false)]
    [InlineData(ParticipationPreset.HumansReadAgentsWrite, ParticipantClassification.Agent, true, true)]
    [InlineData(ParticipationPreset.AgentsOnly, ParticipantClassification.Human, false, false)]
    [InlineData(ParticipationPreset.AgentsOnly, ParticipantClassification.Agent, true, true)]
    [InlineData(ParticipationPreset.Everyone, ParticipantClassification.Human, true, true)]
    public void Participation_rights_follow_the_preset_matrix(ParticipationPreset preset,
        ParticipantClassification classification, bool read, bool write)
    {
        var tangent = ClosedTangent();
        tangent.ChangeParticipationPolicy(Owner, TangentAdmission.Invite, preset, UndeclaredAccess.Deny, Now);
        Assert.Equal((read, write), tangent.ParticipationRights(classification));
    }

    [Theory]
    [InlineData(UndeclaredAccess.Deny, false, false)]
    [InlineData(UndeclaredAccess.Read, true, false)]
    [InlineData(UndeclaredAccess.Write, true, true)]
    public void Undeclared_access_grants_exactly_its_configured_rights(UndeclaredAccess undeclared, bool read, bool write)
    {
        var tangent = ClosedTangent();
        tangent.ChangeParticipationPolicy(Owner, TangentAdmission.Invite, ParticipationPreset.HumansOnly, undeclared, Now);
        Assert.Equal((read, write), tangent.ParticipationRights(ParticipantClassification.Undeclared));
        // The explicit undeclared access is honored even under Everyone.
        tangent.ChangeParticipationPolicy(Owner, TangentAdmission.Invite, ParticipationPreset.Everyone, undeclared, Now.AddMinutes(1));
        Assert.Equal((read, write), tangent.ParticipationRights(ParticipantClassification.Undeclared));
        Assert.Equal((true, true), tangent.ParticipationRights(ParticipantClassification.Human));
        Assert.Equal((true, true), tangent.ParticipationRights(ParticipantClassification.Agent));
    }

    [Fact]
    public void Legacy_records_default_to_undeclared_write_so_current_users_stay_allowed()
    {
        // Records persisted before the policy existed load the zero value, which is Write.
        var untouched = ClosedTangent();
        Assert.Equal(default(UndeclaredAccess), untouched.UndeclaredAccess);
        Assert.Equal(UndeclaredAccess.Write, untouched.UndeclaredAccess);
        Assert.Equal(ParticipationPreset.Everyone, untouched.ParticipationPreset);
        Assert.Equal((true, true), untouched.ParticipationRights(ParticipantClassification.Undeclared));
        Assert.Equal((true, true), untouched.ParticipationRights(ParticipantClassification.Human));
    }

    [Fact]
    public void Participation_policy_changes_remain_with_the_owner_and_bump_the_revision()
    {
        var tangent = OpenTangent();
        Assert.Throws<TangentRuleViolation>(() =>
            tangent.ChangeParticipationPolicy(Member, TangentAdmission.Invite, ParticipationPreset.Everyone, UndeclaredAccess.Deny, Now));
        tangent.ChangeParticipationPolicy(Owner, TangentAdmission.Approval, ParticipationPreset.AgentsOnly, UndeclaredAccess.Read, Now.AddMinutes(1));
        Assert.Equal(TangentAdmission.Approval, tangent.EffectiveAdmission);
        Assert.False(tangent.OpenToSignedIn);
        Assert.Equal(ParticipationPreset.AgentsOnly, tangent.ParticipationPreset);
        Assert.Equal(2, tangent.PolicyRevision);
        Assert.Throws<TangentRuleViolation>(() =>
            tangent.ChangeParticipationPolicy(Owner, (TangentAdmission)99, ParticipationPreset.Everyone, UndeclaredAccess.Deny, Now));
    }

    [Fact]
    public void Scoped_restrictions_change_the_central_room_policy_decision()
    {
        var site = Site();
        var tangent = OpenTangent();
        var room = Room.Create(site, Owner, "restricted-lounge", "Lounge", RoomAdmission.SignedIn, Now, tangent.Id, tangent);
        room.CompleteSpace(site, Owner, room.PolicyRevision, Space("restricted-lounge"), Now, tangent);
        var membership = Role(tangent, Agent, TangentRole.Member);

        // A timeout preserves read access and blocks writing.
        var timedOut = room.CurrentPolicy(site, Agent, null, tangent: tangent, tangentMembership: membership,
            restriction: new EffectiveRestriction(Banned: false));
        Assert.True(timedOut.CanRead);
        Assert.False(timedOut.CanWrite);
        Assert.Equal("allowed", timedOut.Reason);

        // A ban blocks both.
        var banned = room.CurrentPolicy(site, Agent, null, tangent: tangent, tangentMembership: membership,
            restriction: new EffectiveRestriction(Banned: true));
        Assert.False(banned.CanRead);
        Assert.False(banned.CanWrite);
        Assert.False(banned.CanManage);
        Assert.Equal("banned", banned.Reason);

        // The owner is an authority, not a restriction target.
        var owner = room.CurrentPolicy(site, Owner, null, tangent: tangent, restriction: new EffectiveRestriction(Banned: true));
        Assert.Equal("allowed", owner.Reason);
        Assert.True(owner.CanManage);
    }

    [Fact]
    public void A_channel_restriction_never_erases_an_inherited_tangent_ban()
    {
        var ban = new EffectiveRestriction(Banned: true);
        var timeout = new EffectiveRestriction(Banned: false);
        // Any inherited ban survives a channel-scoped timeout or lift.
        Assert.True(Restrictions.Combine(timeout, ban)!.Banned);
        Assert.True(Restrictions.Combine(null, ban)!.Banned);
        Assert.True(Restrictions.Combine(ban, null)!.Banned);
        Assert.True(Restrictions.Combine(ban, timeout)!.Banned);
        // A channel timeout still applies where no ban is inherited; a lift stays local to its scope.
        Assert.False(Restrictions.Combine(timeout, null)!.Banned);
        Assert.Null(Restrictions.Combine(null, null));
    }

    [Fact]
    public void A_members_only_channel_admits_tangent_members_without_a_separate_room_invitation()
    {
        var site = Site();
        var tangent = ClosedTangent();
        var room = Room.Create(site, Owner, "kintsugi-members", "Members", RoomAdmission.InvitationOnly, Now, tangent.Id, tangent);
        room.MembersOnly = true;
        room.CompleteSpace(site, Owner, room.PolicyRevision, Space("kintsugi-members"), Now, tangent);

        // Community membership itself is the invitation, even for an invitation-only channel.
        var member = Role(tangent, Agent, TangentRole.Member);
        var admitted = room.CurrentPolicy(site, Agent, null, tangent: tangent, tangentMembership: member);
        Assert.True(admitted.CanRead);
        Assert.True(admitted.CanWrite);

        // A left participant has no member record and stays outside the members channel.
        var departed = room.CurrentPolicy(site, Other, null, tangent: tangent, tangentMembership: Role(tangent, Other, TangentRole.Left));
        Assert.False(departed.CanRead);
        Assert.Equal("community-membership-required", departed.Reason);

        // Under open admission a members-only channel still excludes participants without a member record.
        var open = OpenTangent();
        var openRoom = Room.Create(site, Owner, "open-members", "Members", RoomAdmission.SignedIn, Now, open.Id, open);
        openRoom.MembersOnly = true;
        openRoom.CompleteSpace(site, Owner, openRoom.PolicyRevision, Space("open-members"), Now, open);
        var outsider = openRoom.CurrentPolicy(site, Other, null, tangent: open);
        Assert.False(outsider.CanRead);
        Assert.Equal("community-membership-required", outsider.Reason);

        // A community reader reads the members channel but still does not write it.
        var reader = openRoom.CurrentPolicy(site, Other, null, tangent: open, tangentMembership: Role(open, Other, TangentRole.Reader));
        Assert.True(reader.CanRead);
        Assert.False(reader.CanWrite);

        // The owner is never gated by the members-only rule.
        Assert.True(openRoom.CurrentPolicy(site, Owner, null, tangent: open).CanRead);
    }

    [Fact]
    public void Scoped_restrictions_remove_delegated_administration_as_well_as_read_and_write()
    {
        var site = Site();
        var tangent = ClosedTangent();
        var adminMembership = Role(tangent, Member, TangentRole.Admin);
        var room = Room.CreateDelegated(site, Member, "kintsugi-admin", "Admin Lounge", RoomAdmission.SignedIn, Now, tangent.Id, tangent);
        room.CompleteSpace(site, Owner, room.PolicyRevision, Space("kintsugi-admin"), Now, tangent);

        // A timed-out administrator keeps read access but loses moderation authority.
        var timedOut = room.CurrentPolicy(site, Member, null, tangent: tangent, tangentMembership: adminMembership,
            restriction: new EffectiveRestriction(Banned: false));
        Assert.True(timedOut.CanRead);
        Assert.False(timedOut.CanWrite);
        Assert.False(timedOut.CanManage);

        // A banned administrator loses everything.
        var banned = room.CurrentPolicy(site, Member, null, tangent: tangent, tangentMembership: adminMembership,
            restriction: new EffectiveRestriction(Banned: true));
        Assert.False(banned.CanRead);
        Assert.False(banned.CanManage);
        Assert.Equal("banned", banned.Reason);

        // Unrestricted, the same administrator manages without owning.
        var clear = room.CurrentPolicy(site, Member, null, tangent: tangent, tangentMembership: adminMembership);
        Assert.True(clear.CanManage);
        Assert.False(clear.IsOwner);

        // The owner remains an authority, not a restriction target.
        var owner = room.CurrentPolicy(site, Owner, null, tangent: tangent, restriction: new EffectiveRestriction(Banned: true));
        Assert.True(owner.CanManage);
        Assert.Equal("allowed", owner.Reason);
    }

    [Fact]
    public void A_delegated_administrator_completes_provisioning_without_gaining_ownership()
    {
        var site = Site();
        var tangent = ClosedTangent();
        var adminMembership = Role(tangent, Member, TangentRole.Admin);
        var room = Room.CreateDelegated(site, Member, "kintsugi-space", "Space", RoomAdmission.SignedIn, Now, tangent.Id, tangent);

        // The delegated creator maps the Space under delegated authority; the owner stays the owner.
        room.CompleteSpace(site, Member, room.PolicyRevision, Space("kintsugi-space"), Now, tangent, adminMembership);
        Assert.Equal(RoomSpaceState.Ready, room.SpaceState);
        Assert.False(room.CurrentPolicy(site, Member, null, tangent: tangent, tangentMembership: adminMembership).IsOwner);

        // An ordinary member and an outsider cannot map the Space.
        var ordinary = Role(tangent, Agent, TangentRole.Member);
        var other = Room.CreateDelegated(site, Member, "kintsugi-other", "Other", RoomAdmission.SignedIn, Now, tangent.Id, tangent);
        Assert.Throws<RoomRuleViolation>(() =>
            other.CompleteSpace(site, Agent, other.PolicyRevision, Space("kintsugi-other"), Now, tangent, ordinary));
        Assert.Throws<RoomRuleViolation>(() =>
            other.CompleteSpace(site, Other, other.PolicyRevision, Space("kintsugi-other"), Now, tangent));
    }

    [Fact]
    public void A_delegated_administrator_sets_the_initial_topic_under_delegated_authority()
    {
        var site = Site();
        var tangent = ClosedTangent();
        var adminMembership = Role(tangent, Member, TangentRole.Admin);
        var room = Room.CreateDelegated(site, Member, "kintsugi-topic", "Topic", RoomAdmission.SignedIn, Now, tangent.Id, tangent);

        // The community membership carries the delegated authority to set the channel topic.
        room.ChangeTopic(site, Member, null, " Delegated welcome ", Now, tangent, adminMembership);
        Assert.Equal("Delegated welcome", room.Topic);

        // Without the membership the same creator has no management authority.
        var bare = Room.CreateDelegated(site, Member, "kintsugi-bare", "Bare", RoomAdmission.SignedIn, Now, tangent.Id, tangent);
        Assert.Throws<RoomRuleViolation>(() => bare.ChangeTopic(site, Member, null, "No authority", Now, tangent));
    }

    private static string Space(string key) => $"at://{Owner}/chat.tangent.space/{key}";
}
