using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Tangent.Activity;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Stewardship;

namespace Tangent.Community;

public sealed partial class ParticipantGovernance
{
    public const int MaximumAdministrationList = 100;

    private static readonly QueryDefinition joinRequests = new() { Sort = SortSpecParser.ParseStrict<TangentJoinRequest>(nameof(TangentJoinRequest.Id)), CountStrategy = CountStrategy.Exact };

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
            signals.Pulse();
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
            await EntityContext.Commit(ct);
            signals.Pulse();
            return new TangentPolicyResult(tangent.Id, tangent.EffectiveAdmission, tangent.ParticipationPreset,
                tangent.UndeclaredAccess, tangent.PolicyRevision);
        }
        finally { gate.Exit(); }
    }
}
