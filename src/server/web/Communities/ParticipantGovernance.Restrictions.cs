using Koan.Data.Core;
using TangentSpace.Activity;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Communities;

public sealed partial class ParticipantGovernance
{
    /// <summary>Scoped timeout/ban/lift. Expiry is checked at evaluation; this never touches global space suspension.</summary>
    public async Task<RestrictionResult> SetRestriction(string actorId, string tangentKey, string? roomKey, string targetIdentifier,
        RestrictionKind kind, DateTimeOffset? until, string reason, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var now = clock.GetUtcNow();
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var tangent = await Tangent.Get(tangentKey, ct)
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorId, ct)
                ?? throw new TangentRuleViolation(TangentDenial.Forbidden, "A verified arrival is required first.");
            // The target arrives as an external identifier; restrictions key by participant id.
            var targetId = (await directory.ByIdentifier(targetIdentifier, ct))?.Participant.Id
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The participant must first establish a verified arrival.");
            var actorMembership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
            TopicRuleViolation? denial = null;
            var scope = RestrictionScope.Tangent;
            var scopeKey = tangentKey;
            long selectedRevision = tangent.PolicyRevision;
            Topic? topic = null;
            TopicAudit? audit = null;
            try
            {
                if (actor.IsSuspended) throw new TopicRuleViolation(TopicDenial.Forbidden, "A suspended participant cannot moderate.");
                // A restricted moderator cannot impose restrictions; the topic path enforces this through CanManage.
                if (string.IsNullOrWhiteSpace(roomKey))
                    await RequireUnrestricted(actorId, tangentKey, now, ct);
                // Higher authority is protected: never the space owner, the Tangent owner, or the acting moderator.
                if (targetId == actorId || space?.IsOwner(targetId) == true || tangent.IsOwner(targetId))
                    throw new TopicRuleViolation(TopicDenial.Forbidden, "Restrictions cannot target the owner or a higher authority.");
                if (!string.IsNullOrWhiteSpace(roomKey))
                {
                    scope = RestrictionScope.Topic;
                    scopeKey = roomKey;
                    topic = await Topic.Get(roomKey, ct) ?? throw new TopicRuleViolation(TopicDenial.NotFound, "The requested channel does not exist.");
                    if (topic.TangentKey != tangentKey)
                        throw new TopicRuleViolation(TopicDenial.InvalidInput, "The channel does not belong to this Tangent.");
                    selectedRevision = topic.PolicyRevision;
                    var membership = await TopicMembership.Get(TopicMembership.Key(topic.Id, actorId), ct);
                    var tangentMembership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
                    var restriction = await Restrictions.ForTopic(actorId, topic.Id, tangentKey, now, ct);
                    var policy = topic.CurrentPolicy(space, actorId, membership, actor.IsSuspended, tangent, tangentMembership,
                        actor.Classification, restriction);
                    if (!policy.CanManage)
                        throw new TopicRuleViolation(TopicDenial.Forbidden, "Only a channel manager or the Tangent stewards can restrict here.");
                }
                else if (!(tangent.IsOwner(actorId) || await IsServerOwner(actorId, ct)) && actorMembership?.Role != TangentRole.Admin)
                    throw new TopicRuleViolation(TopicDenial.Forbidden, "Only the Tangent owner or a Tangent administrator can restrict at this scope.");
                var stored = ScopedRestriction.Impose(scope, scopeKey, targetId, kind, until, reason, actorId, now);
                await stored.Save(ct);
                await ActivityJournal.AppendInTransaction(ActivityKind.RestrictionChanged, roomKey ?? "", actorId, targetId,
                    tangentKey: tangent.Id, ct: ct);
                audit = new TopicAudit
                {
                    ActorParticipantId = actorId, RoomKey = roomKey ?? "", TargetParticipantId = targetId, Operation = TopicAdministration.SetRestriction,
                    Accepted = true, Reason = stored.Reason, SelectedPolicyRevision = selectedRevision,
                    SpacePolicyRevision = space?.PolicyRevision ?? 0, OccurredAt = now
                };
                await audit.Save(ct);
                await CommandCommit.Report(new RestrictionResult(scope, scopeKey, targetId, stored.Kind, stored.Until, stored.Reason, audit.Id), ct);
                await EntityContext.Commit(ct);
                ActivityJournal.SignalAfterCommit();
                return new RestrictionResult(scope, scopeKey, targetId, stored.Kind, stored.Until, stored.Reason, audit.Id);
            }
            catch (TopicRuleViolation rejected) { denial = rejected; }
            audit = new TopicAudit
            {
                ActorParticipantId = actorId, RoomKey = roomKey ?? "", TargetParticipantId = targetId, Operation = TopicAdministration.SetRestriction,
                Accepted = false, Denial = denial!.Denial, Reason = denial.Message,
                SelectedPolicyRevision = selectedRevision, SpacePolicyRevision = space?.PolicyRevision ?? 0, OccurredAt = now
            };
            await audit.Save(ct);
            await EntityContext.Commit(ct);
            throw new TangentRuleViolation(denial.Denial switch
            {
                TopicDenial.NotFound => TangentDenial.NotFound,
                TopicDenial.InvalidInput => TangentDenial.InvalidInput,
                TopicDenial.AlreadyExists => TangentDenial.AlreadyExists,
                TopicDenial.MembershipMismatch => TangentDenial.MembershipMismatch,
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
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            var scope = string.IsNullOrWhiteSpace(roomKey) ? RestrictionScope.Tangent : RestrictionScope.Topic;
            var scopeKey = scope == RestrictionScope.Topic ? roomKey! : tangentKey;
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
                    var (_, policy) = await TopicWithPolicy(actorId, scopeKey, tangentKey, ct);
                    if (!policy.CanManage)
                        throw new TangentRuleViolation(TangentDenial.Forbidden, "Only channel managers can inspect channel restrictions.");
                }
            }
            await EntityContext.Commit(ct);
            return new RestrictionResult(scope, scopeKey, stored.ParticipantId, stored.Kind, stored.Until, stored.Reason, "");
        }
        finally { gate.Exit(); }
    }
}
