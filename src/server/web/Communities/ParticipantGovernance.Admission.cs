using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using TangentSpace.Activity;
using TangentSpace.Infrastructure;
using TangentSpace.Rooms;

namespace TangentSpace.Communities;

public sealed partial class ParticipantGovernance
{
    public const int MaximumAdministrationList = 100;

    private static readonly QueryDefinition invitations = new() { Sort = SortSpecParser.ParseStrict<TangentInvitation>(nameof(TangentInvitation.Id)), CountStrategy = CountStrategy.Exact };

    private static readonly QueryDefinition joinRequests = new() { Sort = SortSpecParser.ParseStrict<TangentJoinRequest>(nameof(TangentJoinRequest.Id)), CountStrategy = CountStrategy.Exact };

    /// <summary>Creates a bound invitation. Issuing never joins anyone, never sends a post, and delivery stays out of scope.
    /// The target arrives as an external identifier resolved to its current holder here.</summary>
    public async Task<TangentInvitationResult> Invite(string actorId, string tangentKey, string targetIdentifier, ParticipantRole role, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var now = clock.GetUtcNow();
            var (tangent, actorMembership, _) = await LoadTangentContext(actorId, tangentKey, ct);
            var target = await RequireTarget(tangent, targetIdentifier, actorId, ct);
            var granted = RoleOf(role);
            // A delegated administrator invites members and readers; only the owner grants administrators.
            if (!(tangent.IsOwner(actorId) || await IsServerOwner(actorId, ct)) && (granted == TangentRole.Admin || actorMembership?.Role != TangentRole.Admin))
                throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the Tangent owner or a Tangent administrator can invite, and only the owner grants administrators.");
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
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
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
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
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
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
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
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
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

    /// <summary>Owner-only admission and classification policy. Enforcement is central and applies to every reader.</summary>
    public async Task<TangentPolicyResult> SetParticipationPolicy(string actorId, string tangentKey, TangentAdmission admission,
        ParticipationPreset preset, UndeclaredAccess undeclared, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
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

    private static TangentInvitationResult Describe(TangentInvitation invitation) => new(invitation.Id, invitation.TangentKey,
        invitation.RecipientParticipantId, invitation.GrantedRole, invitation.IssuedAt, invitation.ExpiresAt,
        invitation.RevokedAt is not null, invitation.RedeemedAt is not null, Delivered: false);
}
