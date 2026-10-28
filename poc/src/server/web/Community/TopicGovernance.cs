using Koan.Data.Core;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Spaces;
using Tangent.Activity;
using Tangent.Access;
using Tangent.Stewardship;
using Tangent.Conversation;

namespace Tangent.Community;

/// <summary>Register once as a singleton; policy and acceptance share the host's arrival gate.</summary>
public sealed class TopicGovernance(TimeProvider clock, PolicyGate gate, ParticipantDirectory directory,
    TangentRoleAccess roleAccess, SignalBus signals, TangentGovernance tangents)
{
    public async Task<TopicListing> List(string? actorId, int page, CancellationToken ct)
    {
        if (page is < 1 or > TopicConstants.MaximumPage)
            throw new TopicRuleViolation(TopicDenial.InvalidInput, "Choose a topic page between 1 and 10000.");
        var result = await tangents.ListTopicDirectory(actorId, null, page, ct);
        return new TopicListing(result.Topics, page, result.NextPage, clock.GetUtcNow(), result.ScanLimited);
    }

    public async Task<TopicDescription?> Describe(string? actorId, string topicKey, CancellationToken ct)
    {
        var read = await ReadAsync(actorId, topicKey, ct);
        var policy = actorId is null && read.Topic?.IsPubliclyReadable(read.Tangent) == true
            ? read.Policy with { CanRead = true, Reason = "allowed" }
            : read.Policy;
        return read.Topic is null || (!policy.CanRead && !policy.CanManage)
            ? null
            : TopicDescription.From(read.Topic, policy, read.Tangent);
    }

    /// <summary>Bounded actor-filtered topic directory for one Tangent.</summary>
    public async Task<TangentTopicDirectory> ListForTangent(string? actorId, string tangentKey, int page, CancellationToken ct)
    {
        if (page is < 1 or > TopicConstants.MaximumPage)
            throw new TopicRuleViolation(TopicDenial.InvalidInput, "Choose a topic page between 1 and 10000.");
        return await tangents.ListTopicDirectory(actorId, tangentKey, page, ct);
    }

    public async Task<TopicAdministrationResult> SetSuspension(string actorId, string targetIdentifier, bool suspended, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var participant = (await directory.ByIdentifier(targetIdentifier, ct))?.Participant;
            var targetId = participant?.Id;
            var actor = await Participant.Get(actorId, ct);
            var denial = space?.IsOwner(actorId) != true || actor?.IsSuspended == true || targetId is null || space.IsOwner(targetId)
                ? new TopicRuleViolation(TopicDenial.Forbidden, "Only the current space owner can suspend or restore other participants.")
                : participant is null ? new TopicRuleViolation(TopicDenial.NotFound, "The participant must first establish a verified arrival.") : null;
            if (denial is null)
            {
                participant!.IsSuspended = suspended;
                space!.PolicyRevision = checked(space.PolicyRevision + 1);
                await participant.Save(ct);
                await space.Save(ct);
                await ActivityJournal.AppendInTransaction(ActivityKind.ParticipantChanged, "", actorId, targetId, ct: ct);
            }
            var audit = new TopicAudit
            {
                ActorParticipantId = actorId, TargetParticipantId = targetId, Operation = TopicAdministration.SetSuspension,
                Accepted = denial is null, Denial = denial?.Denial,
                Reason = denial?.Message ?? (suspended ? "Participant suspended." : "Participant restored."),
                SpacePolicyRevision = space?.PolicyRevision ?? 0, OccurredAt = clock.GetUtcNow()
            };
            await audit.Save(ct);
            await EntityContext.Commit(ct);
            if (denial is null) signals.Pulse();
            return new TopicAdministrationResult(audit.Accepted, audit.Denial, audit.Reason, "",
                audit.SpacePolicyRevision, audit.Id);
        }
        finally { gate.Exit(); }
    }

    public Task<TopicAdministrationResult> SetMembership(string actorId, string topicKey, string targetIdentifier, TopicRole role, CancellationToken ct)
        => Administer(actorId, topicKey, targetIdentifier, TopicAdministration.SetMembership, role,
            (space, topic, actor, targetId, target, tangent, tangentMembership, now) =>
            {
                var current = RequireTopic(topic);
                return new Change(current, current.ChangeMembership(actorId, targetId!, target, role, now, tangent, tangentMembership));
            }, ct);

    public async Task<TopicMembershipPreview> PreviewMembership(string actorId, string topicKey, string tangentKey,
        string targetIdentifier, TopicRole role, CancellationToken ct)
    {
        if (!Enum.IsDefined(role))
            throw new TopicRuleViolation(TopicDenial.InvalidInput, "Choose a Topic membership role.");
        using var fresh = EntityContext.NoCache();
        var topic = await Topic.Get(topicKey, ct);
        if (topic is null || topic.TangentKey != tangentKey)
            throw new TopicRuleViolation(TopicDenial.NotFound, "The requested Topic does not exist in this Tangent.");
        var actorPolicy = await ReadPolicyAsync(actorId, topicKey, ct);
        if (!actorPolicy.CanManage)
            throw new TopicRuleViolation(TopicDenial.Forbidden, "Only a Topic administrator can preview participation here.");
        var participant = (await directory.ByIdentifier(targetIdentifier, ct))?.Participant
            ?? throw new TopicRuleViolation(TopicDenial.NotFound, "This person must first arrive on this Host with a verified account.");
        var tangent = await Community.Tangent.Get(tangentKey, ct)
            ?? throw new TopicRuleViolation(TopicDenial.NotFound, "The Tangent no longer exists.");
        if (tangent.IsOwner(participant.Id))
            throw new TopicRuleViolation(TopicDenial.Forbidden, "The Tangent owner’s membership cannot be changed here.");
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var tangentMembership = await TangentMembership.Get(TangentMembership.Key(tangentKey, participant.Id), ct);
        var proposed = TopicMembership.Assign(topic, participant.Id, role, actorId, clock.GetUtcNow());
        var restriction = await Restrictions.ForTopic(participant.Id, topicKey, tangentKey, clock.GetUtcNow(), ct);
        var selected = topic.CurrentPolicy(space, participant.Id, proposed, participant.IsSuspended,
            tangent, tangentMembership, participant.Classification, restriction);
        var effective = await Project(topic, tangent, selected, participant, restriction, ct);
        var obstacle = effective.CanRead ? null : effective.Reason switch
        {
            "community-membership-required" or "not-admitted" => "This person is not admitted to the Tangent. Admit them there first; a Topic role cannot widen Tangent admission.",
            "participation-policy" => "The Tangent’s participation policy does not admit this person’s classification. Change that Tangent policy first.",
            "suspended" => "This person is suspended from the Host. Restore their account before granting access.",
            "banned" => "A current restriction prevents this person from reading this conversation.",
            "removed" => "Removing Topic access prevents this account from participating here.",
            "role-required" => "The inherited Tangent or Topic access rule still prevents reading. Review Who can read in advanced rules.",
            _ => "The current Host, Tangent, or Topic policy prevents reading."
        };
        if (obstacle is null && !effective.CanWrite && role is TopicRole.Member or TopicRole.Manager)
            obstacle = topic.IsLocked ? "This conversation is locked to new Posts. Unlock it before this person can reply."
                : tangentMembership?.Role == TangentRole.Reader
                    ? "The Tangent’s participation or inherited posting rule still prevents replies. Review Who can reply in advanced rules."
                    : "The inherited Tangent or Topic posting rule still prevents replies. Review Who can reply in advanced rules.";
        return new TopicMembershipPreview(participant.Id, await directory.BestLabel(participant.Id, ct), role,
            effective.CanRead, effective.CanWrite, effective.CanManage, obstacle);
    }

    public Task<TopicAdministrationResult> SetTopic(string actorId, string topicKey, string description, CancellationToken ct)
        => Administer(actorId, topicKey, null, TopicAdministration.SetTopic, null,
            (space, topic, actor, _, _, tangent, tangentMembership, now) =>
            {
                var current = RequireTopic(topic);
                current.ChangeDescription(description, now);
                return new Change(current);
            }, ct);

    public Task<TopicAdministrationResult> SetReadAudience(string actorId, string topicKey, TopicReadAudience audience,
        bool publishExistingHistory, CancellationToken ct)
        => Administer(actorId, topicKey, null, TopicAdministration.SetReadAudience, null,
            (space, topic, _, _, _, tangent, _, now) =>
            {
                var current = RequireTopic(topic);
                current.ChangeReadAudience(audience, publishExistingHistory, now, tangent);
                return new Change(current);
            }, ct, audience, publishExistingHistory);

    public Task<TopicAdministrationResult> SetSettings(string actorId, string topicKey, bool? allowPostEditing, bool? isLocked,
        string? title, string? description, CancellationToken ct)
        => Administer(actorId, topicKey, null, TopicAdministration.SetSettings, null,
            (space, topic, actor, _, _, tangent, tangentMembership, now) =>
            {
                var current = RequireTopic(topic);
                current.ChangeSettings(allowPostEditing, isLocked, title, description, now);
                return new Change(current);
            }, ct);

    public async Task<AccessMapView> GetAccess(string actorId, string topicKey, string? expectedTangentKey, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var topic = await Topic.Get(topicKey, ct)
            ?? throw new TopicRuleViolation(TopicDenial.NotFound, "The requested Topic does not exist.");
        if (expectedTangentKey is not null && topic.TangentKey != expectedTangentKey)
            throw new TopicRuleViolation(TopicDenial.NotFound, "The requested Topic does not exist in this Tangent.");
        var participant = await Participant.Get(actorId, ct);
        var membership = await TopicMembership.Get(TopicMembership.Key(topicKey, actorId), ct);
        var tangent = await Community.Tangent.Get(topic.TangentKey, ct)
            ?? throw new TopicRuleViolation(TopicDenial.NotFound, "The Topic's Tangent does not exist.");
        var tangentMembership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
        var restriction = await Restrictions.ForTopic(actorId, topic.Id, topic.TangentKey, clock.GetUtcNow(), ct);
        var policy = await Project(topic, tangent, topic.CurrentPolicy(space, actorId, membership, participant?.IsSuspended == true, tangent,
            tangentMembership, participant?.Classification ?? ParticipantClassification.Undeclared, restriction), participant, restriction, ct);
        if (!policy.CanManage)
            throw new TopicRuleViolation(TopicDenial.Forbidden, "Only a Topic administrator can inspect its access settings.");
        var selected = (topic.Access ?? AccessMap.TopicDefaults()).NormalizeForTopic();
        var parent = tangent.Access ?? AccessMap.TangentDefaults();
        var inherited = new List<string>(3);
        if (selected.See is null) inherited.Add("see");
        if (selected.Post is null) inherited.Add("post");
        if (selected.Manage is null) inherited.Add("manage");
        return new AccessMapView(selected, selected.ResolveTopic(parent), inherited, parent.ResolveTangent());
    }

    public Task<TopicAdministrationResult> SetAccess(string actorId, string topicKey, AccessMap access, CancellationToken ct,
        bool publishExistingHistory = false)
        => Administer(actorId, topicKey, null, TopicAdministration.SetAccess, null,
            (space, topic, actor, _, _, tangent, tangentMembership, now) =>
            {
                var current = RequireTopic(topic);
                current.ChangeAccess(access, now, tangent, publishExistingHistory);
                return new Change(current);
            }, ct, publishExistingHistory: publishExistingHistory);

    /// <summary>One projected policy with the entities it was read from. Lock-free.</summary>
    public sealed record TopicReadContext(TopicPolicy Policy, Topic? Topic, Tangent? Tangent);

    /// <summary>
    /// Current authority for read paths: no write gate and no transaction. A concurrent
    /// change surfaces on the next read; nothing here may save.
    /// A denied snapshot still carries the selected revisions.
    /// </summary>
    public Task<TopicReadContext> ReadAsync(string? actorId, string topicKey, CancellationToken ct)
        => ReadContext(actorId, topicKey, ignoreTimeout: false, ct);

    /// <summary>Authors retain their own history unless suspended. Moderation history follows
    /// current effective Topic authority; a timeout pauses actions, not this read.</summary>
    public async Task<bool> CanReadHistory(string actorId, Post post, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var participant = await Participant.Get(actorId, ct);
        if (participant is null || participant.IsSuspended) return false;
        if (post.AuthorParticipantId == actorId) return true;
        var current = await ReadContext(actorId, post.TopicKey, ignoreTimeout: true, ct);
        return current.Policy.CanRead && current.Policy.CanManage;
    }

    private async Task<TopicReadContext> ReadContext(string? actorId, string topicKey, bool ignoreTimeout, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var topic = await Topic.Get(topicKey, ct);
        var membership = topic is null || actorId is null ? null
            : await TopicMembership.Get(TopicMembership.Key(topicKey, actorId), ct);
        var participant = actorId is null ? null : await Participant.Get(actorId, ct);
        var tangent = topic is null ? null : await Community.Tangent.Get(topic.TangentKey, ct);
        var tangentMembership = actorId is null || tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
        var restriction = topic is null || actorId is null ? null
            : await Restrictions.ForTopic(actorId, topic.Id, topic.TangentKey, clock.GetUtcNow(), ct);
        if (ignoreTimeout && restriction is { Banned: false }) restriction = null;
        var policy = topic is null ? null : await Project(topic, tangent, topic.CurrentPolicy(space, actorId, membership, participant?.IsSuspended == true, tangent, tangentMembership,
            participant?.Classification ?? ParticipantClassification.Undeclared, restriction), participant, restriction, ct);
        policy ??= new TopicPolicy(topicKey, actorId, 0, space?.PolicyRevision ?? 0,
                null, false, false, false, false, "topic-not-found");
        return new TopicReadContext(policy, topic, tangent);
    }

    /// <summary>Current authority for a read that needs nothing but the decision.</summary>
    public async Task<TopicPolicy> ReadPolicyAsync(string? actorId, string topicKey, CancellationToken ct)
        => (await ReadAsync(actorId, topicKey, ct)).Policy;

    /// <summary>
    /// The acceptance envelope for mutations: the write gate, one transaction, and the
    /// policy check inside — the caller's action owns this transaction and its commit.
    /// Do not call PDS/network services, nest a transaction, or reenter TopicGovernance/Arrival inside the callback.
    /// A denied snapshot still carries the selected revisions.
    /// </summary>
    public async Task<T> WithinAcceptanceAsync<T>(string? actorId, string topicKey,
        Func<TopicPolicy, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            var read = await ReadAsync(actorId, topicKey, ct);
            var result = await operation(read.Policy, ct);
            await EntityContext.Commit(ct);
            return result;
        }
        finally { gate.Exit(); }
    }

    private async Task<TopicAdministrationResult> Administer(string actorId, string topicKey, string? targetIdentifier,
        TopicAdministration operation, TopicRole? requestedRole,
        Func<Space?, Topic?, TopicMembership?, string?, TopicMembership?, Tangent?, TangentMembership?, DateTimeOffset, Change> apply,
        CancellationToken ct, TopicReadAudience? requestedReadAudience = null, bool? publishExistingHistory = null)
    {
        TopicAdministrationResult result;
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var now = clock.GetUtcNow();
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            // Administration targets arrive as external identifiers and resolve to participant ids here.
            var targetId = targetIdentifier is null ? null
                : (await directory.ByIdentifier(targetIdentifier, ct))?.Participant.Id
                ?? throw new TopicRuleViolation(TopicDenial.NotFound, "The participant must first establish a verified arrival.");
            Topic? topic = null;
            Change? change = null;
            TopicRuleViolation? denial = null;
            try
            {
                Topic.CheckKey(topicKey);
                topic = await Topic.Get(topicKey, ct);
                var actorParticipant = await Participant.Get(actorId, ct);
                if (!await SpaceAdmission.Admitted(space, actorId, ct)) throw new TopicRuleViolation(TopicDenial.Forbidden, "Join this Space before administering topics.");
                if (actorParticipant?.IsSuspended == true)
                    throw new TopicRuleViolation(TopicDenial.Forbidden, "A suspended participant cannot administer topics.");
                // Scoped restrictions apply to administration as well as conversation: a banned or timed-out
                // participant cannot administer here. Owners are never restriction targets.
                if (topic is not null && await Restrictions.ForTopic(actorId, topic.Id, topic.TangentKey, now, ct) is not null)
                    throw new TopicRuleViolation(TopicDenial.Forbidden, "A scoped restriction currently denies this administration.");
                var actor = topic is null ? null : await TopicMembership.Get(TopicMembership.Key(topicKey, actorId), ct);
                var target = topic is null || targetId is null ? null
                    : await TopicMembership.Get(TopicMembership.Key(topicKey, targetId), ct);
                var tangent = topic is null ? null : await Community.Tangent.Get(topic.TangentKey, ct);
                var tangentMembership = tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
                if (topic is not null)
                {
                    var restriction = await Restrictions.ForTopic(actorId, topic.Id, topic.TangentKey, now, ct);
                    var projected = await Project(topic, tangent,
                        topic.CurrentPolicy(space, actorId, actor, false, tangent, tangentMembership,
                            actorParticipant?.Classification ?? ParticipantClassification.Undeclared, restriction),
                        actorParticipant, restriction, ct);
                    var allowed = projected.CanManage;
                    if (!allowed) throw new TopicRuleViolation(TopicDenial.Forbidden, "The current role does not permit this Topic operation.");
                }
                // The domain command, membership row and audit use the same once-resolved identity.
                change = apply(space, topic, actor, targetId, target, tangent, tangentMembership, now);
            }
            catch (TopicRuleViolation rejected) { denial = rejected; }

            // Policy errors are audited denials. Persistence failures propagate; Koan's deferred
            // writes do not promise an atomic rollback across the Topic, membership and journal.
            if (change is not null)
            {
                topic = change.Topic;
                await topic.Save(ct);
                if (change.Membership is not null) await change.Membership.Save(ct);
                var kind = operation == TopicAdministration.SetMembership ? ActivityKind.MembershipChanged : ActivityKind.TopicChanged;
                await ActivityJournal.AppendInTransaction(kind, topic.Id, actorId, change.Membership?.ParticipantId, topic.TangentKey, ct: ct);
            }
            var audit = new TopicAudit
            {
                ActorParticipantId = actorId, TopicKey = topicKey, TargetParticipantId = targetId, Operation = operation,
                RequestedRole = requestedRole, RequestedReadAudience = requestedReadAudience,
                PublishExistingHistory = publishExistingHistory, Accepted = denial is null, Denial = denial?.Denial,
                Reason = denial?.Message ?? "Accepted.", SelectedPolicyRevision = topic?.PolicyRevision ?? 0,
                SpacePolicyRevision = space?.PolicyRevision ?? 0, OccurredAt = now
            };
            await audit.Save(ct);
            await EntityContext.Commit(ct);
            if (change is not null) signals.Pulse();
            result = new TopicAdministrationResult(audit.Accepted, audit.Denial, audit.Reason, topicKey,
                audit.SelectedPolicyRevision, audit.Id);
        }
        finally { gate.Exit(); }
        return result;
    }

    private static Topic RequireTopic(Topic? topic)
        => topic ?? throw new TopicRuleViolation(TopicDenial.NotFound, "The requested topic does not exist.");

    private Task<TopicPolicy> Project(Topic topic, Tangent? tangent, TopicPolicy policy,
        Participant? participant, EffectiveRestriction? restriction, CancellationToken ct)
        => roleAccess.ProjectTopic(topic, tangent, policy, participant?.IsSuspended == true, restriction,
            participant?.Classification ?? ParticipantClassification.Undeclared, ct);

    private sealed record Change(Topic Topic, TopicMembership? Membership = null);
}

public sealed record TopicMembershipPreview(string ParticipantId, string Label, TopicRole Role, bool CanRead,
    bool CanReply, bool CanManage, string? Obstacle);
