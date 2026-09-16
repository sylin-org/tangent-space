using Koan.Data.Core;
using Tangent.Activity;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Stewardship;

namespace Tangent.Community;

public sealed partial class ParticipantGovernance
{
    /// <summary>Self-join is idempotent for members, honest about pending approval, and never leaks private tangents.</summary>
    public async Task<JoinResult> Join(string actorId, string tangentKey, string? invitationId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tangentKey);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
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
                return await CommitJoin(new JoinResult(JoinOutcome.AlreadyMember,
                    membership is null ? null : MembershipOf(membership), null), ct);
            if (invitation is not null)
            {
                invitation.Redeem(actorId, now);
                await invitation.Save(ct);
                var redeemed = TangentMembership.Assign(tangent, actorId, invitation.GrantedRole, actorId, now);
                await redeemed.Save(ct);
                await Journal(ActivityKind.MembershipChanged, actorId, actorId, tangentKey, ct);
                await Journal(ActivityKind.InvitationChanged, actorId, invitation.RecipientParticipantId, tangentKey, ct);
                return await CommitJoin(new JoinResult(JoinOutcome.Joined, MembershipOf(redeemed), null), ct);
            }
            if (tangent.EffectiveAdmission == TangentAdmission.Open)
            {
                var joined = TangentMembership.Assign(tangent, actorId, TangentRole.Member, actorId, now);
                await joined.Save(ct);
                await Journal(ActivityKind.MembershipChanged, actorId, actorId, tangentKey, ct);
                return await CommitJoin(new JoinResult(JoinOutcome.Joined, MembershipOf(joined), null), ct);
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
                return await CommitJoin(new JoinResult(JoinOutcome.PendingApproval, null, request.Id), ct);
            }
            throw new TangentRuleViolation(TangentDenial.Forbidden, "This Tangent admits new participants by invitation only.");
        }
        finally { gate.Exit(); }
    }

    /// <summary>Voluntary departure persisted as Left, distinct from an admin removal; the owner cannot abandon a Tangent.</summary>
    public async Task<LeaveResult> Leave(string actorId, string tangentKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tangentKey);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
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
            await CommandCommit.Report(new LeaveResult(LeaveOutcome.Left, MembershipOf(departed)), ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return new LeaveResult(LeaveOutcome.Left, MembershipOf(departed));
        }
        finally { gate.Exit(); }
    }

    /// <summary>Exact-scope role change. Topic scope delegates to the audited topic membership guards.</summary>
    public async Task<RoleChangeResult> SetRole(string actorId, string tangentKey, string? roomKey, string targetIdentifier, ParticipantRole role, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(roomKey)) return new RoleChangeResult(null, await SetTopicRole(actorId, tangentKey, roomKey, targetIdentifier, role, ct));
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var now = clock.GetUtcNow();
            var (tangent, actorMembership, _) = await LoadTangentContext(actorId, tangentKey, ct);
            var target = await RequireTarget(tangent, targetIdentifier, actorId, ct);
            await RequireUnrestricted(actorId, tangentKey, now, ct);
            var granted = RoleOf(role);
            var targetMembership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, target), ct);
            if (!(tangent.IsOwner(actorId) || await IsServerOwner(actorId, ct)))
            {
                if (actorMembership?.Role != TangentRole.Admin)
                    throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the Tangent owner or a Tangent administrator can change roles here.");
                // A delegated administrator never grants or reshapes administrator authority.
                if (granted == TangentRole.Admin || targetMembership?.Role == TangentRole.Admin)
                    throw new TangentRuleViolation(TangentDenial.Forbidden, "A Tangent administrator cannot grant or change administrator roles.");
            }
            var assigned = TangentMembership.Assign(tangent, target, granted, actorId, now);
            await assigned.Save(ct);
            await Journal(ActivityKind.MembershipChanged, actorId, target, tangent.Id, ct);
            return await CommitRole(new RoleChangeResult(MembershipOf(assigned), null), ct);
        }
        finally { gate.Exit(); }
    }

    private async Task<TopicAdministrationResult> SetTopicRole(string actorId, string tangentKey, string roomKey, string targetIdentifier, ParticipantRole role, CancellationToken ct)
    {
        // Scope is exact: the audited topic guards reject cross-scope changes; verify the topic belongs to the Tangent first.
        // This path runs before this service enters its own gate, so TopicGovernance can take it freely.
        var described = await topics.Describe(actorId, roomKey, ct);
        if (described is null) throw new TangentRuleViolation(TangentDenial.NotFound, "The requested topic does not exist.");
        if (described.TangentKey != tangentKey)
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "The topic does not belong to this Tangent.");
        var mapped = role switch
        {
            ParticipantRole.Admin => TopicRole.Manager,
            ParticipantRole.Member => TopicRole.Member,
            _ => TopicRole.Reader
        };
        try
        {
            var result = await topics.SetMembership(actorId, roomKey, targetIdentifier, mapped, ct);
            if (!result.Accepted) throw new TopicRuleViolation(result.Denial ?? TopicDenial.Forbidden, result.Reason);
            return result;
        }
        catch (TopicRuleViolation rejected)
        {
            throw new TangentRuleViolation(rejected.Denial switch
            {
                TopicDenial.NotFound => TangentDenial.NotFound,
                TopicDenial.InvalidInput => TangentDenial.InvalidInput,
                TopicDenial.AlreadyExists => TangentDenial.AlreadyExists,
                TopicDenial.MembershipMismatch => TangentDenial.MembershipMismatch,
                _ => TangentDenial.Forbidden
            }, rejected.Message);
        }
    }

    private static TangentRole RoleOf(ParticipantRole role) => role switch
    {
        ParticipantRole.Admin => TangentRole.Admin,
        ParticipantRole.Member => TangentRole.Member,
        _ => TangentRole.Reader
    };

    private static TangentMembershipResult MembershipOf(TangentMembership membership)
        => new(membership.TangentKey, membership.ParticipantId, membership.Role);

    private async Task<JoinResult> CommitJoin(JoinResult result, CancellationToken ct)
    {
        await CommandCommit.Report(result, ct);
        await EntityContext.Commit(ct);
        ActivityJournal.SignalAfterCommit();
        return result;
    }

    private async Task<RoleChangeResult> CommitRole(RoleChangeResult result, CancellationToken ct)
    {
        await CommandCommit.Report(result, ct);
        await EntityContext.Commit(ct);
        ActivityJournal.SignalAfterCommit();
        return result;
    }

    private async Task<LeaveResult> AlreadyLeft(TangentMembership? membership, CancellationToken ct)
    {
        var result = new LeaveResult(LeaveOutcome.AlreadyNotMember, membership is null ? null : MembershipOf(membership));
        await CommandCommit.Report(result, ct);
        await EntityContext.Commit(ct);
        return result;
    }
}
