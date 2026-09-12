using CarpaNet.Identity;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using TangentSpace.Activity;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Communities;

/// <summary>
/// Companion participation operations for the inbound MCP boundary: self join/leave, bound invitations,
/// manual admission, scoped roles, participation policy, scoped restrictions and personal watch state.
/// Every mutation runs under the host policy gate with the durable journal; access enforcement itself stays
/// central in Room.CurrentPolicy so browser HTTP and MCP observe exactly the same decisions.
/// </summary>
public sealed class CompanionGovernance(TimeProvider clock, PolicyGate gate, RoomGovernance rooms,
    TangentSpace.Participants.ParticipantDirectory directory)
{
    public const int MaximumAdministrationList = 100;
    private static readonly QueryDefinition invitations = new() { Sort = SortSpecParser.ParseStrict<TangentInvitation>(nameof(TangentInvitation.Id)), CountStrategy = CountStrategy.Exact };
    private static readonly QueryDefinition joinRequests = new() { Sort = SortSpecParser.ParseStrict<TangentJoinRequest>(nameof(TangentJoinRequest.Id)), CountStrategy = CountStrategy.Exact };

    /// <summary>
    /// Read-only current-authority check for replay gates: an active, unrestricted steward at Tangent scope
    /// (owner or community administrator) or any current channel manager at room scope. Never mutates or
    /// journals; scoped restrictions and suspension always answer false so saved private administration
    /// data is not replayed to a participant who just lost authority.
    /// </summary>
    public async Task<bool> CanAdminister(string actorId, string tangentKey, string? roomKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tangentKey);
        if (!TangentSpace.Participants.Participant.IsValidId(actorId))
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "A verified participant is required.");
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var now = clock.GetUtcNow();
            var participant = await Participant.Get(actorId, ct);
            if (participant is null || participant.IsSuspended) { await EntityContext.Commit(ct); return false; }
            if (!string.IsNullOrWhiteSpace(roomKey))
            {
                var site = await TangentSite.Get(TangentConstants.SiteId, ct);
                var room = await Room.Get(roomKey, ct);
                if (room is null || room.TangentKey != tangentKey) { await EntityContext.Commit(ct); return false; }
                var membership = await RoomMembership.Get(RoomMembership.Key(room.Id, actorId), ct);
                var tangent = await TangentCommunity.Get(tangentKey, ct);
                var tangentMembership = tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
                var restriction = await Restrictions.ForRoom(actorId, room.Id, tangentKey, now, ct);
                var policy = room.CurrentPolicy(site, actorId, membership, participant.IsSuspended, tangent, tangentMembership,
                    participant.Classification, restriction);
                await EntityContext.Commit(ct);
                return policy.CanManage;
            }
            var target = await TangentCommunity.Get(tangentKey, ct);
            if (target is null) { await EntityContext.Commit(ct); return false; }
            var steward = (target.IsOwner(actorId) || await IsServerOwner(actorId, ct))
                || await TangentMembership.Get(TangentMembership.Key(tangentKey, actorId), ct) is { Role: TangentRole.Admin };
            // Scoped restrictions apply to administration as well; owners are never restriction targets.
            var restricted = await Restrictions.ForTangent(actorId, tangentKey, now, ct) is not null;
            await EntityContext.Commit(ct);
            return steward && !restricted;
        }
        finally { gate.Exit(); }
    }

    /// <summary>Self-join is idempotent for members, honest about pending approval, and never leaks private tangents.</summary>
    public async Task<CompanionJoinResult> Join(string actorId, string tangentKey, string? invitationId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tangentKey);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var now = clock.GetUtcNow();
            var (tangent, membership, participant) = await LoadTangentContext(actorId, tangentKey, ct);
            var invitation = await ResolveInvitation(invitationId, actorId, tangentKey, now, ct);
            // A durable admin removal is an override no self-join can bypass; only an owner/admin-issued
            // invitation (checked below) restores a removed participant under issuer authority.
            if (membership?.Role == TangentRole.Removed && invitation is null)
                throw new TangentRuleViolation(TangentDenial.Forbidden, "A removed participant cannot rejoin on their own.");
            // Private (invite-only) tangents unknown to this actor answer as not found; they are not listed either.
            if (!tangent.CanParticipate(actorId, membership) && invitation is null
                && tangent.EffectiveAdmission is not (TangentAdmission.Open or TangentAdmission.Approval))
                throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            if (await Restrictions.ForTangent(actorId, tangentKey, now, ct) is not null)
                throw new TangentRuleViolation(TangentDenial.Forbidden, "A scoped restriction currently denies this participation.");
            if ((tangent.IsOwner(actorId) || await IsServerOwner(actorId, ct)) || membership?.Role is TangentRole.Member or TangentRole.Admin or TangentRole.Reader)
                return await CommitJoin(new CompanionJoinResult(CompanionJoinOutcome.AlreadyMember,
                    membership is null ? null : MembershipOf(membership), null), ct);
            if (invitation is not null)
            {
                invitation.Redeem(actorId, now);
                await invitation.Save(ct);
                var redeemed = TangentMembership.Assign(tangent, actorId, invitation.GrantedRole, actorId, now);
                await redeemed.Save(ct);
                await Journal(ActivityKind.MembershipChanged, actorId, actorId, tangentKey, ct);
                await Journal(ActivityKind.InvitationChanged, actorId, invitation.RecipientParticipantId, tangentKey, ct);
                return await CommitJoin(new CompanionJoinResult(CompanionJoinOutcome.Joined, MembershipOf(redeemed), null), ct);
            }
            if (tangent.EffectiveAdmission == TangentAdmission.Open)
            {
                var joined = TangentMembership.Assign(tangent, actorId, TangentRole.Member, actorId, now);
                await joined.Save(ct);
                await Journal(ActivityKind.MembershipChanged, actorId, actorId, tangentKey, ct);
                return await CommitJoin(new CompanionJoinResult(CompanionJoinOutcome.Joined, MembershipOf(joined), null), ct);
            }
            if (tangent.EffectiveAdmission == TangentAdmission.Approval)
            {
                var request = await TangentJoinRequest.Get(TangentJoinRequest.Key(tangentKey, actorId), ct);
                if (request is null)
                {
                    request = TangentJoinRequest.Open(tangentKey, actorId, now);
                    await request.Save(ct);
                    await Journal(ActivityKind.MembershipChanged, actorId, actorId, tangentKey, ct);
                }
                else if (!request.Pending && request.Accepted != true)
                    throw new TangentRuleViolation(TangentDenial.Forbidden, "A previous admission request for this Tangent was declined.");
                else if (!request.Pending)
                {
                    // Accepted earlier and the participant left again: reopen a truthful pending request.
                    request = TangentJoinRequest.Open(tangentKey, actorId, now);
                    await request.Save(ct);
                    await Journal(ActivityKind.MembershipChanged, actorId, actorId, tangentKey, ct);
                }
                return await CommitJoin(new CompanionJoinResult(CompanionJoinOutcome.PendingApproval, null, request.Id), ct);
            }
            throw new TangentRuleViolation(TangentDenial.Forbidden, "This Tangent admits new participants by invitation only.");
        }
        finally { gate.Exit(); }
    }

    /// <summary>Voluntary departure persisted as Left, distinct from an admin removal; the owner cannot abandon a Tangent.</summary>
    public async Task<CompanionLeaveResult> Leave(string actorId, string tangentKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tangentKey);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var now = clock.GetUtcNow();
            var (tangent, membership, participant) = await LoadTangentContext(actorId, tangentKey, ct);
            if ((tangent.IsOwner(actorId) || await IsServerOwner(actorId, ct)))
                throw new TangentRuleViolation(TangentDenial.Forbidden, "The owner cannot leave; transfer ownership first.");
            if (membership is null || membership.Role is TangentRole.Left or TangentRole.Removed)
                return await AlreadyLeft(membership, ct);
            // Leaving never deletes history: public source records and read state are retained.
            var departed = TangentMembership.Assign(tangent, actorId, TangentRole.Left, actorId, now);
            await departed.Save(ct);
            await Journal(ActivityKind.MembershipChanged, actorId, actorId, tangentKey, ct);
            await CommandCommit.Report(new CompanionLeaveResult(CompanionLeaveOutcome.Left, MembershipOf(departed)), ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return new CompanionLeaveResult(CompanionLeaveOutcome.Left, MembershipOf(departed));
        }
        finally { gate.Exit(); }
    }

    /// <summary>Creates a bound invitation. Issuing never joins anyone, never sends a message, and delivery stays out of scope.
    /// The target arrives as an external identifier resolved to its current holder here.</summary>
    public async Task<TangentInvitationResult> Invite(string actorId, string tangentKey, string targetIdentifier, CompanionRole role, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var now = clock.GetUtcNow();
            var (tangent, actorMembership, _) = await LoadTangentContext(actorId, tangentKey, ct);
            var target = await RequireTarget(tangent, targetIdentifier, actorId, ct);
            var granted = RoleOf(role);
            // A delegated administrator invites members and readers; only the owner grants administrators.
            if (!(tangent.IsOwner(actorId) || await IsServerOwner(actorId, ct)) && (granted == TangentRole.Admin || actorMembership?.Role != TangentRole.Admin))
                throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the Tangent owner or a community administrator can invite, and only the owner grants administrators.");
            await RequireUnrestricted(actorId, tangentKey, now, ct);
            var invitation = TangentInvitation.Issue(tangent, target, granted, actorId, now);
            await invitation.Save(ct);
            await Journal(ActivityKind.InvitationChanged, actorId, target, tangentKey, ct);
            await CommandCommit.Report(Describe(invitation), ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return Describe(invitation);
        }
        finally { gate.Exit(); }
    }

    public async Task<TangentInvitationResult> RevokeInvitation(string actorId, string invitationId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invitationId);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var now = clock.GetUtcNow();
            var invitation = await TangentInvitation.Get(invitationId, ct)
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested invitation does not exist.");
            var (tangent, actorMembership, _) = await LoadTangentContext(actorId, invitation.TangentKey, ct);
            await RequireSteward(tangent, actorId, actorMembership, "revoke invitations", ct);
            await RequireUnrestricted(actorId, invitation.TangentKey, now, ct);
            invitation.Revoke(actorId, now);
            await invitation.Save(ct);
            await Journal(ActivityKind.InvitationChanged, actorId, invitation.RecipientParticipantId, invitation.TangentKey, ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return Describe(invitation);
        }
        finally { gate.Exit(); }
    }

    public async Task<IReadOnlyList<TangentInvitationResult>> ListInvitations(string actorId, string tangentKey, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var (tangent, actorMembership, _) = await LoadTangentContext(actorId, tangentKey, ct);
            await RequireSteward(tangent, actorId, actorMembership, "inspect invitations", ct);
            await RequireUnrestricted(actorId, tangentKey, clock.GetUtcNow(), ct);
            var listed = (await TangentInvitation.Query(value => value.TangentKey == tangentKey,
                invitations.WithPagination(1, MaximumAdministrationList), ct)).Select(Describe).ToArray();
            await EntityContext.Commit(ct);
            return listed;
        }
        finally { gate.Exit(); }
    }

    /// <summary>Pending admission requests are durable and inspectable by the Tangent's stewards.</summary>
    public async Task<IReadOnlyList<TangentJoinRequestSummary>> ListJoinRequests(string actorId, string tangentKey, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var (tangent, actorMembership, _) = await LoadTangentContext(actorId, tangentKey, ct);
            await RequireSteward(tangent, actorId, actorMembership, "inspect admission requests", ct);
            await RequireUnrestricted(actorId, tangentKey, clock.GetUtcNow(), ct);
            var listed = (await TangentJoinRequest.Query(value => value.TangentKey == tangentKey,
                joinRequests.WithPagination(1, MaximumAdministrationList), ct))
                .Select(request => new TangentJoinRequestSummary(request.Id, request.TangentKey, request.ParticipantId,
                    request.RequestedAt, request.DecidedAt is not null, request.Accepted))
                .ToArray();
            await EntityContext.Commit(ct);
            return listed;
        }
        finally { gate.Exit(); }
    }

    public async Task<TangentJoinRequestDecision> DecideJoinRequest(string actorId, string requestId, bool accept, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var now = clock.GetUtcNow();
            var request = await TangentJoinRequest.Get(requestId, ct)
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested admission request does not exist.");
            if (!request.Pending)
                throw new TangentRuleViolation(TangentDenial.InvalidInput, "This admission request was already decided.");
            var (tangent, actorMembership, _) = await LoadTangentContext(actorId, request.TangentKey, ct);
            await RequireSteward(tangent, actorId, actorMembership, "decide admission requests", ct);
            await RequireUnrestricted(actorId, request.TangentKey, now, ct);
            TangentMembershipResult? membership = null;
            if (accept)
            {
                // Re-check the target's current state: a later removal or active restriction must survive an approval.
                var target = await TangentMembership.Get(TangentMembership.Key(tangent.Id, request.ParticipantId), ct);
                if (target?.Role == TangentRole.Removed)
                    throw new TangentRuleViolation(TangentDenial.InvalidInput, "This participant was removed after requesting admission.");
                if (await Restrictions.ForTangent(request.ParticipantId, tangent.Id, now, ct) is not null)
                    throw new TangentRuleViolation(TangentDenial.InvalidInput, "This participant is currently restricted.");
                var admitted = TangentMembership.Assign(tangent, request.ParticipantId, TangentRole.Member, actorId, now);
                await admitted.Save(ct);
                membership = MembershipOf(admitted);
                await Journal(ActivityKind.MembershipChanged, actorId, request.ParticipantId, tangent.Id, ct);
            }
            request.Decide(actorId, accept, now);
            await request.Save(ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return new TangentJoinRequestDecision(request.Id, accept, membership);
        }
        finally { gate.Exit(); }
    }

    /// <summary>Exact-scope role change. Channel scope delegates to the audited room membership guards.</summary>
    public async Task<CompanionRoleResult> SetRole(string actorId, string tangentKey, string? roomKey, string targetIdentifier, CompanionRole role, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(roomKey)) return new CompanionRoleResult(null, await SetChannelRole(actorId, tangentKey, roomKey, targetIdentifier, role, ct));
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var now = clock.GetUtcNow();
            var (tangent, actorMembership, _) = await LoadTangentContext(actorId, tangentKey, ct);
            var target = await RequireTarget(tangent, targetIdentifier, actorId, ct);
            await RequireUnrestricted(actorId, tangentKey, now, ct);
            var granted = RoleOf(role);
            var targetMembership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, target), ct);
            if (!(tangent.IsOwner(actorId) || await IsServerOwner(actorId, ct)))
            {
                if (actorMembership?.Role != TangentRole.Admin)
                    throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the Tangent owner or a community administrator can change roles here.");
                // A delegated administrator never grants or reshapes administrator authority.
                if (granted == TangentRole.Admin || targetMembership?.Role == TangentRole.Admin)
                    throw new TangentRuleViolation(TangentDenial.Forbidden, "A community administrator cannot grant or change administrator roles.");
            }
            var assigned = TangentMembership.Assign(tangent, target, granted, actorId, now);
            await assigned.Save(ct);
            await Journal(ActivityKind.MembershipChanged, actorId, target, tangent.Id, ct);
            return await CommitRole(new CompanionRoleResult(MembershipOf(assigned), null), ct);
        }
        finally { gate.Exit(); }
    }

    /// <summary>Owner-only admission and classification policy. Enforcement is central and applies to every reader.</summary>
    public async Task<TangentPolicyResult> SetParticipationPolicy(string actorId, string tangentKey, TangentAdmission admission,
        ParticipationPreset preset, UndeclaredAccess undeclared, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var (tangent, _, _) = await LoadTangentContext(actorId, tangentKey, ct);
            tangent.ChangeParticipationPolicy(actorId, admission, preset, undeclared, clock.GetUtcNow());
            await tangent.Save(ct);
            await Journal(ActivityKind.TangentChanged, actorId, null, tangentKey, ct);
            await CommandCommit.Report(new TangentPolicyResult(tangent.Id, tangent.EffectiveAdmission, tangent.ParticipationPreset, tangent.UndeclaredAccess, tangent.PolicyRevision), ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return new TangentPolicyResult(tangent.Id, tangent.EffectiveAdmission, tangent.ParticipationPreset,
                tangent.UndeclaredAccess, tangent.PolicyRevision);
        }
        finally { gate.Exit(); }
    }

    /// <summary>Scoped timeout/ban/lift. Expiry is checked at evaluation; this never touches global site suspension.</summary>
    public async Task<RestrictionResult> SetRestriction(string actorId, string tangentKey, string? roomKey, string targetIdentifier,
        RestrictionKind kind, DateTimeOffset? until, string reason, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var now = clock.GetUtcNow();
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            var tangent = await TangentCommunity.Get(tangentKey, ct)
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorId, ct)
                ?? throw new TangentRuleViolation(TangentDenial.Forbidden, "A verified arrival is required first.");
            // The target arrives as an external identifier; restrictions key by participant id.
            var targetId = (await directory.ByIdentifier(targetIdentifier, ct))?.Participant.Id
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The participant must first establish a verified arrival.");
            var actorMembership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
            RoomRuleViolation? denial = null;
            var scope = RestrictionScope.Tangent;
            var scopeKey = tangentKey;
            long selectedRevision = tangent.PolicyRevision;
            Room? room = null;
            RoomAudit? audit = null;
            try
            {
                if (actor.IsSuspended) throw new RoomRuleViolation(RoomDenial.Forbidden, "A suspended participant cannot moderate.");
                // A restricted moderator cannot impose restrictions; the room path enforces this through CanManage.
                if (string.IsNullOrWhiteSpace(roomKey))
                    await RequireUnrestricted(actorId, tangentKey, now, ct);
                // Higher authority is protected: never the site owner, the Tangent owner, or the acting moderator.
                if (targetId == actorId || site?.IsOwner(targetId) == true || tangent.IsOwner(targetId))
                    throw new RoomRuleViolation(RoomDenial.Forbidden, "Restrictions cannot target the owner or a higher authority.");
                if (!string.IsNullOrWhiteSpace(roomKey))
                {
                    scope = RestrictionScope.Room;
                    scopeKey = roomKey;
                    room = await Room.Get(roomKey, ct) ?? throw new RoomRuleViolation(RoomDenial.NotFound, "The requested channel does not exist.");
                    if (room.TangentKey != tangentKey)
                        throw new RoomRuleViolation(RoomDenial.InvalidInput, "The channel does not belong to this Tangent.");
                    selectedRevision = room.PolicyRevision;
                    var membership = await RoomMembership.Get(RoomMembership.Key(room.Id, actorId), ct);
                    var tangentMembership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
                    var restriction = await Restrictions.ForRoom(actorId, room.Id, tangentKey, now, ct);
                    var policy = room.CurrentPolicy(site, actorId, membership, actor.IsSuspended, tangent, tangentMembership,
                        actor.Classification, restriction);
                    if (!policy.CanManage)
                        throw new RoomRuleViolation(RoomDenial.Forbidden, "Only a channel manager or the Tangent stewards can restrict here.");
                }
                else if (!(tangent.IsOwner(actorId) || await IsServerOwner(actorId, ct)) && actorMembership?.Role != TangentRole.Admin)
                    throw new RoomRuleViolation(RoomDenial.Forbidden, "Only the Tangent owner or a community administrator can restrict at this scope.");
                var stored = ScopedRestriction.Impose(scope, scopeKey, targetId, kind, until, reason, actorId, now);
                await stored.Save(ct);
                await ActivityJournal.AppendInTransaction(ActivityKind.RestrictionChanged, roomKey ?? "", actorId, targetId,
                    tangentKey: tangent.Id, ct: ct);
                audit = new RoomAudit
                {
                    ActorParticipantId = actorId, RoomKey = roomKey ?? "", TargetParticipantId = targetId, Operation = RoomAdministration.SetRestriction,
                    Accepted = true, Reason = stored.Reason, SelectedPolicyRevision = selectedRevision,
                    SitePolicyRevision = site?.PolicyRevision ?? 0, OccurredAt = now
                };
                await audit.Save(ct);
                await CommandCommit.Report(new RestrictionResult(scope, scopeKey, targetId, stored.Kind, stored.Until, stored.Reason, audit.Id), ct);
                await EntityContext.Commit(ct);
                ActivityJournal.SignalAfterCommit();
                return new RestrictionResult(scope, scopeKey, targetId, stored.Kind, stored.Until, stored.Reason, audit.Id);
            }
            catch (RoomRuleViolation rejected) { denial = rejected; }
            audit = new RoomAudit
            {
                ActorParticipantId = actorId, RoomKey = roomKey ?? "", TargetParticipantId = targetId, Operation = RoomAdministration.SetRestriction,
                Accepted = false, Denial = denial!.Denial, Reason = denial.Message,
                SelectedPolicyRevision = selectedRevision, SitePolicyRevision = site?.PolicyRevision ?? 0, OccurredAt = now
            };
            await audit.Save(ct);
            await EntityContext.Commit(ct);
            throw new TangentRuleViolation(denial.Denial switch
            {
                RoomDenial.NotFound => TangentDenial.NotFound,
                RoomDenial.InvalidInput => TangentDenial.InvalidInput,
                RoomDenial.AlreadyExists => TangentDenial.AlreadyExists,
                RoomDenial.MembershipMismatch => TangentDenial.MembershipMismatch,
                _ => TangentDenial.Forbidden
            }, denial.Message);
        }
        finally { gate.Exit(); }
    }

    /// <summary>Current stored moderation record; a participant may inspect their own, stewards any at their scope.</summary>
    public async Task<RestrictionResult?> GetRestriction(string actorId, string tangentKey, string? roomKey, string targetIdentifier, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var scope = string.IsNullOrWhiteSpace(roomKey) ? RestrictionScope.Tangent : RestrictionScope.Room;
            var scopeKey = scope == RestrictionScope.Room ? roomKey! : tangentKey;
            var targetId = (await directory.ByIdentifier(targetIdentifier, ct))?.Participant.Id;
            var stored = targetId is null ? null : await ScopedRestriction.Get(ScopedRestriction.Key(scope, scopeKey, targetId), ct);
            if (stored is null) { await EntityContext.Commit(ct); return null; }
            if (actorId != stored.ParticipantId)
            {
                if (scope == RestrictionScope.Tangent)
                {
                    var (tangent, actorMembership, _) = await LoadTangentContext(actorId, tangentKey, ct);
                    await RequireSteward(tangent, actorId, actorMembership, "inspect restrictions", ct);
                }
                else
                {
                    var (_, policy) = await RoomWithPolicy(actorId, scopeKey, tangentKey, ct);
                    if (!policy.CanManage)
                        throw new TangentRuleViolation(TangentDenial.Forbidden, "Only channel managers can inspect channel restrictions.");
                }
            }
            await EntityContext.Commit(ct);
            return new RestrictionResult(scope, scopeKey, stored.ParticipantId, stored.Kind, stored.Until, stored.Reason, "");
        }
        finally { gate.Exit(); }
    }

    /// <summary>Personal interest only: never joins, never changes admission, never touches read positions or access.</summary>
    public async Task<WatchResult> SetWatch(string actorId, string roomKey, WatchMode mode, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var (room, policy) = await RoomWithPolicy(actorId, roomKey, null, ct);
            if (!policy.CanRead)
                throw new TangentRuleViolation(TangentDenial.Forbidden, "Watch state needs read access to the channel.");
            var setting = WatchSetting.Choose(actorId, roomKey, mode, actorId, clock.GetUtcNow());
            await setting.Save(ct);
            await CommandCommit.Report(new WatchResult(roomKey, setting.Mode), ct);
            await EntityContext.Commit(ct);
            return new WatchResult(roomKey, setting.Mode);
        }
        finally { gate.Exit(); }
    }

    /// <summary>Personal Tangent-wide default; each channel's explicit watch preference still overrides it.</summary>
    public async Task<WatchResult> SetTangentWatch(string actorId, string tangentKey, WatchMode mode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tangentKey);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            // A Tangent default is a preference, not an access change: no policy check, just an active arrival.
            var participant = await Participant.Get(actorId, ct);
            if (participant is null || participant.IsSuspended)
                throw new TangentRuleViolation(TangentDenial.Forbidden, "An active verified arrival is required.");
            _ = await TangentCommunity.Get(tangentKey, ct)
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var setting = TangentWatchSetting.Choose(actorId, tangentKey, mode, actorId, clock.GetUtcNow());
            await setting.Save(ct);
            await CommandCommit.Report(new WatchResult(tangentKey, setting.Mode), ct);
            await EntityContext.Commit(ct);
            return new WatchResult(tangentKey, setting.Mode);
        }
        finally { gate.Exit(); }
    }

    /// <summary>Prototype owner-reviewed declaration. No label fetch, no inference; undeclared stays its own state.</summary>
    public async Task<ParticipantClassification> DeclareClassification(string actorId, string targetIdentifier, ParticipantClassification classification, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            if (site?.IsOwner(actorId) != true)
                throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the site owner records classification declarations in this prototype.");
            var participant = (await directory.ByIdentifier(targetIdentifier, ct))?.Participant
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The participant must first establish a verified arrival.");
            participant.Declare(classification);
            await participant.Save(ct);
            await Journal(ActivityKind.ParticipantChanged, actorId, participant.Id, null, ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return participant.Classification;
        }
        finally { gate.Exit(); }
    }

    private async Task<RoomAdministrationResult> SetChannelRole(string actorId, string tangentKey, string roomKey, string targetIdentifier, CompanionRole role, CancellationToken ct)
    {
        // Scope is exact: the audited room guards reject cross-scope changes; verify the channel belongs to the Tangent first.
        // This path runs before this service enters its own gate, so RoomGovernance can take it freely.
        var described = await rooms.Describe(actorId, roomKey, ct);
        if (described is null) throw new TangentRuleViolation(TangentDenial.NotFound, "The requested channel does not exist.");
        if (described.TangentKey != tangentKey)
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "The channel does not belong to this Tangent.");
        var mapped = role switch
        {
            CompanionRole.Admin => RoomRole.Manager,
            CompanionRole.Member => RoomRole.Member,
            _ => RoomRole.Reader
        };
        try
        {
            var result = await rooms.SetMembership(actorId, roomKey, targetIdentifier, mapped, ct);
            if (!result.Accepted) throw new RoomRuleViolation(result.Denial ?? RoomDenial.Forbidden, result.Reason);
            return result;
        }
        catch (RoomRuleViolation rejected)
        {
            throw new TangentRuleViolation(rejected.Denial switch
            {
                RoomDenial.NotFound => TangentDenial.NotFound,
                RoomDenial.InvalidInput => TangentDenial.InvalidInput,
                RoomDenial.AlreadyExists => TangentDenial.AlreadyExists,
                RoomDenial.MembershipMismatch => TangentDenial.MembershipMismatch,
                _ => TangentDenial.Forbidden
            }, rejected.Message);
        }
    }

    private async Task<(TangentCommunity Tangent, TangentMembership? Membership, Participant Participant)> LoadTangentContext(
        string actorId, string tangentKey, CancellationToken ct)
    {
        if (!TangentSpace.Participants.Participant.IsValidId(actorId))
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "A verified participant is required.");
        var tangent = await TangentCommunity.Get(tangentKey, ct)
            ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
        var membership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
        var participant = await Participant.Get(actorId, ct);
        if (participant is null || participant.IsSuspended)
            throw new TangentRuleViolation(TangentDenial.Forbidden, "Joining and governing require an active verified arrival.");
        return (tangent, membership, participant);
    }

    private static async Task<bool> IsServerOwner(string did, CancellationToken ct)
        => (await TangentSite.Get(TangentConstants.SiteId, ct))?.IsOwner(did) == true;

    /// <summary>Resolves one external identifier to its holder and refuses protected targets.
    /// A DID with no local arrival yet mints its participant through the same enrollment path
    /// (invitations bind holders who have never signed in here). Returns the participant id.</summary>
    private async Task<string> RequireTarget(TangentCommunity tangent, string targetIdentifier, string actorId, CancellationToken ct)
    {
        var target = CarpaNet.Identity.IdentityResolver.IsValidDid(targetIdentifier)
            ? await directory.EnsureAtproto(targetIdentifier, ct)
            : (await directory.ByIdentifier(targetIdentifier, ct))?.Participant
            ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The participant must first establish a verified arrival.");
        if (tangent.IsOwner(target.Id) || await IsServerOwner(target.Id, ct))
            throw new TangentRuleViolation(TangentDenial.Forbidden, "The Tangent owner's standing cannot be changed through this operation.");
        if (target.Id == actorId)
            throw new TangentRuleViolation(TangentDenial.Forbidden, "A participant cannot change their own standing this way.");
        return target.Id;
    }

    private async Task RequireSteward(TangentCommunity tangent, string actorId, TangentMembership? actorMembership, string purpose, CancellationToken ct)
    {
        if (!(tangent.IsOwner(actorId) || await IsServerOwner(actorId, ct)) && actorMembership?.Role != TangentRole.Admin)
            throw new TangentRuleViolation(TangentDenial.Forbidden, $"Only the Tangent owner or a community administrator can {purpose}.");
    }

    /// <summary>Scoped restrictions apply to administration as well as conversation: a banned or timed-out
    /// steward cannot moderate. Owners are never restriction targets, so this never denies one.</summary>
    private async Task RequireUnrestricted(string actorId, string tangentKey, DateTimeOffset now, CancellationToken ct)
    {
        if (await Restrictions.ForTangent(actorId, tangentKey, now, ct) is not null)
            throw new TangentRuleViolation(TangentDenial.Forbidden, "A scoped restriction currently denies this administration.");
    }

    private async Task<TangentInvitation?> ResolveInvitation(string? invitationId, string actorId, string tangentKey, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(invitationId)) return null;
        var invitation = await TangentInvitation.Get(invitationId, ct)
            ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested invitation does not exist.");
        if (invitation.TangentKey != tangentKey)
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "The invitation does not match this Tangent.");
        if (invitation.RecipientParticipantId != actorId)
            throw new TangentRuleViolation(TangentDenial.Forbidden, "The invitation was issued for another participant.");
        if (invitation.RevokedAt is not null)
            throw new TangentRuleViolation(TangentDenial.Forbidden, "The invitation was revoked.");
        if (invitation.RedeemedAt is not null)
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "The invitation was already redeemed.");
        if (invitation.ExpiresAt <= now)
            throw new TangentRuleViolation(TangentDenial.Forbidden, "The invitation expired.");
        return invitation;
    }

    private async Task<(Room Room, RoomPolicy Policy)> RoomWithPolicy(string actorId, string roomKey, string? expectedTangentKey, CancellationToken ct)
    {
        var site = await TangentSite.Get(TangentConstants.SiteId, ct);
        await TangentBootstrap.EnsureHome(site, clock, ct);
        var room = await Room.Get(roomKey, ct) ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested channel does not exist.");
        if (expectedTangentKey is not null && room.TangentKey != expectedTangentKey)
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "The channel does not belong to this Tangent.");
        var participant = await Participant.Get(actorId, ct);
        if (participant is null || participant.IsSuspended)
            throw new TangentRuleViolation(TangentDenial.Forbidden, "An active verified arrival is required.");
        var membership = await RoomMembership.Get(RoomMembership.Key(room.Id, actorId), ct);
        var tangent = await TangentCommunity.Get(room.TangentKey, ct);
        var tangentMembership = tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
        var restriction = await Restrictions.ForRoom(actorId, room.Id, room.TangentKey, clock.GetUtcNow(), ct);
        var policy = room.CurrentPolicy(site, actorId, membership, participant.IsSuspended, tangent, tangentMembership,
            participant.Classification, restriction);
        return (room, policy);
    }

    private static TangentRole RoleOf(CompanionRole role) => role switch
    {
        CompanionRole.Admin => TangentRole.Admin,
        CompanionRole.Member => TangentRole.Member,
        _ => TangentRole.Reader
    };

    private static TangentMembershipResult MembershipOf(TangentMembership membership)
        => new(membership.TangentKey, membership.ParticipantId, membership.Role);

    private static TangentInvitationResult Describe(TangentInvitation invitation) => new(invitation.Id, invitation.TangentKey,
        invitation.RecipientParticipantId, invitation.GrantedRole, invitation.IssuedAt, invitation.ExpiresAt,
        invitation.RevokedAt is not null, invitation.RedeemedAt is not null, Delivered: false);

    private async Task Journal(ActivityKind kind, string? actorId, string? targetIdentifier, string? tangentKey, CancellationToken ct)
        => await ActivityJournal.AppendInTransaction(kind, "", actorId, targetIdentifier, tangentKey, ct: ct);

    private async Task<CompanionJoinResult> CommitJoin(CompanionJoinResult result, CancellationToken ct)
    {
        await CommandCommit.Report(result, ct);
        await EntityContext.Commit(ct);
        ActivityJournal.SignalAfterCommit();
        return result;
    }

    private async Task<CompanionRoleResult> CommitRole(CompanionRoleResult result, CancellationToken ct)
    {
        await CommandCommit.Report(result, ct);
        await EntityContext.Commit(ct);
        ActivityJournal.SignalAfterCommit();
        return result;
    }

    private async Task<CompanionLeaveResult> AlreadyLeft(TangentMembership? membership, CancellationToken ct)
    {
        var result = new CompanionLeaveResult(CompanionLeaveOutcome.AlreadyNotMember, membership is null ? null : MembershipOf(membership));
        await CommandCommit.Report(result, ct);
        await EntityContext.Commit(ct);
        return result;
    }
}
