using Koan.Data.Core;
using Tangent.Activity;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Stewardship;
using Tangent.Spaces;

namespace Tangent.Community;

/// <summary>
/// How a participant takes part in a Tangent, and the substrate every one of those operations shares:
/// load the Tangent and the actor's standing in it, require a steward, refuse a restricted one, and
/// journal what happened. Access enforcement itself is not here — it stays central in Topic.CurrentPolicy,
/// so the browser and the connector observe exactly the same decisions.
///
/// The operations live in this type's other files: Membership, Admission, Restrictions, Watches and
/// Classification. They are partial files rather than separate services because the substrate below is
/// shared by all of them, and it is a hand-rolled command pipeline that R3.3 replaces with the real one.
/// </summary>
public sealed partial class ParticipantGovernance(TimeProvider clock, PolicyGate gate, TopicGovernance topics,
    ParticipantDirectory directory)
{
    /// <summary>
    /// Read-only current-authority check for replay gates: an active, unrestricted steward at Tangent scope
    /// (owner or Tangent administrator) or any current topic manager at topic scope. Never mutates or
    /// journals; scoped restrictions and suspension always answer false so saved private administration
    /// data is not replayed to a participant who just lost authority.
    /// </summary>
    public async Task<bool> CanAdminister(string actorId, string tangentKey, string? roomKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tangentKey);
        if (!Participant.IsValidId(actorId))
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "A verified participant is required.");
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            var now = clock.GetUtcNow();
            var participant = await Participant.Get(actorId, ct);
            if (participant is null || participant.IsSuspended) { await EntityContext.Commit(ct); return false; }
            if (!string.IsNullOrWhiteSpace(roomKey))
            {
                var space = await Space.Get(TangentConstants.SpaceId, ct);
                var topic = await Topic.Get(roomKey, ct);
                if (topic is null || topic.TangentKey != tangentKey) { await EntityContext.Commit(ct); return false; }
                var membership = await TopicMembership.Get(TopicMembership.Key(topic.Id, actorId), ct);
                var tangent = await Community.Tangent.Get(tangentKey, ct);
                var tangentMembership = tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
                var restriction = await Restrictions.ForTopic(actorId, topic.Id, tangentKey, now, ct);
                var policy = topic.CurrentPolicy(space, actorId, membership, participant.IsSuspended, tangent, tangentMembership,
                    participant.Classification, restriction);
                await EntityContext.Commit(ct);
                return policy.CanManage;
            }
            var target = await Community.Tangent.Get(tangentKey, ct);
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

    private async Task<(Tangent Tangent, TangentMembership? Membership, Participant Participant)> LoadTangentContext(
        string actorId, string tangentKey, CancellationToken ct)
    {
        if (!Participant.IsValidId(actorId))
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "A verified participant is required.");
        var tangent = await Community.Tangent.Get(tangentKey, ct)
            ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
        var membership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
        var participant = await Participant.Get(actorId, ct);
        if (participant is null || participant.IsSuspended)
            throw new TangentRuleViolation(TangentDenial.Forbidden, "Joining and governing require an active verified arrival.");
        return (tangent, membership, participant);
    }

    private static async Task<bool> IsServerOwner(string did, CancellationToken ct)
        => (await Space.Get(TangentConstants.SpaceId, ct))?.IsOwner(did) == true;

    /// <summary>Resolves one external identifier to its holder and refuses protected targets.
    /// A DID with no local arrival yet mints its participant through the same enrollment path
    /// (invitations bind holders who have never signed in here). Returns the participant id.</summary>
    private async Task<string> RequireTarget(Tangent tangent, string targetIdentifier, string actorId, CancellationToken ct)
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

    private async Task RequireSteward(Tangent tangent, string actorId, TangentMembership? actorMembership, string purpose, CancellationToken ct)
    {
        if (!(tangent.IsOwner(actorId) || await IsServerOwner(actorId, ct)) && actorMembership?.Role != TangentRole.Admin)
            throw new TangentRuleViolation(TangentDenial.Forbidden, $"Only the Tangent owner or a Tangent administrator can {purpose}.");
    }

    /// <summary>Scoped restrictions apply to administration as well as conversation: a banned or timed-out
    /// steward cannot moderate. Owners are never restriction targets, so this never denies one.</summary>
    private async Task RequireUnrestricted(string actorId, string tangentKey, DateTimeOffset now, CancellationToken ct)
    {
        if (await Restrictions.ForTangent(actorId, tangentKey, now, ct) is not null)
            throw new TangentRuleViolation(TangentDenial.Forbidden, "A scoped restriction currently denies this administration.");
    }

    private async Task<(Topic Topic, TopicPolicy Policy)> TopicWithPolicy(string actorId, string roomKey, string? expectedTangentKey, CancellationToken ct)
    {
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var topic = await Topic.Get(roomKey, ct) ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested topic does not exist.");
        if (expectedTangentKey is not null && topic.TangentKey != expectedTangentKey)
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "The topic does not belong to this Tangent.");
        var participant = await Participant.Get(actorId, ct);
        if (participant is null || participant.IsSuspended)
            throw new TangentRuleViolation(TangentDenial.Forbidden, "An active verified arrival is required.");
        var membership = await TopicMembership.Get(TopicMembership.Key(topic.Id, actorId), ct);
        var tangent = await Community.Tangent.Get(topic.TangentKey, ct);
        var tangentMembership = tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
        var restriction = await Restrictions.ForTopic(actorId, topic.Id, topic.TangentKey, clock.GetUtcNow(), ct);
        var policy = topic.CurrentPolicy(space, actorId, membership, participant.IsSuspended, tangent, tangentMembership,
            participant.Classification, restriction);
        return (topic, policy);
    }

    private async Task Journal(ActivityKind kind, string? actorId, string? targetIdentifier, string? tangentKey, CancellationToken ct)
        => await ActivityJournal.AppendInTransaction(kind, "", actorId, targetIdentifier, tangentKey, ct: ct);
}
