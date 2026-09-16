using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Spaces;
using Tangent.Activity;
using Tangent.Access;
using Tangent.Stewardship;

namespace Tangent.Community;

/// <summary>Register once as a singleton; policy and acceptance share the host's arrival gate.</summary>
public sealed class TopicGovernance(TimeProvider clock, PolicyGate gate, ParticipantDirectory directory,
    TangentRoleAccess roleAccess)
{
    private static readonly QueryDefinition directoryQuery = new QueryDefinition
    {
        Sort = SortSpecParser.ParseStrict<Topic>(nameof(Topic.Id)),
        CountStrategy = CountStrategy.Exact
    };

    public async Task<TopicListing> List(string? actorId, int page, CancellationToken ct)
    {
        if (page is < 1 or > TopicConstants.MaximumPage)
            throw new TopicRuleViolation(TopicDenial.InvalidInput, "Choose a topic page between 1 and 10000.");
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var participant = actorId is null ? null : await Participant.Get(actorId, ct);
            var result = await Topic.AllWithCount(directoryQuery.WithPagination(page, TopicConstants.PageSize), ct);
            var now = clock.GetUtcNow();
            var descriptions = new List<TopicDescription>(result.Items.Count);
            foreach (var topic in result.Items)
            {
                var membership = actorId is null ? null : await TopicMembership.Get(TopicMembership.Key(topic.Id, actorId), ct);
                var tangent = await Community.Tangent.Get(topic.TangentKey, ct);
                var tangentMembership = actorId is null || tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
                var restriction = actorId is null ? null : await Restrictions.ForTopic(actorId, topic.Id, topic.TangentKey, now, ct);
                var policy = await Project(topic, tangent, topic.CurrentPolicy(space, actorId, membership, participant?.IsSuspended == true, tangent, tangentMembership,
                    participant?.Classification ?? ParticipantClassification.Undeclared, restriction), participant, restriction, ct);
                // A directory must not become an oracle for invitation-only channel names.
                if (policy.CanRead || policy.CanManage) descriptions.Add(TopicDescription.From(topic, policy));
            }
            await EntityContext.Commit(ct);
            return new TopicListing(descriptions, page, result.HasNextPage ? page + 1 : null, clock.GetUtcNow());
        }
        finally { gate.Exit(); }
    }

    public Task<TopicDescription?> Describe(string? actorId, string roomKey, CancellationToken ct)
        => WithCurrentPolicy<TopicDescription?>(actorId, roomKey, async (policy, token) =>
        {
            var topic = await Topic.Get(roomKey, token);
            return topic is null || (!policy.CanRead && !policy.CanManage) ? null : TopicDescription.From(topic, policy);
        }, ct);

    /// <summary>Bounded actor-filtered topic directory for one Tangent.</summary>
    public async Task<TangentChannelDirectory> ListForTangent(string? actorId, string tangentKey, int page, CancellationToken ct)
    {
        if (page is < 1 or > TopicConstants.MaximumPage)
            throw new TopicRuleViolation(TopicDenial.InvalidInput, "Choose a topic page between 1 and 10000.");
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var participant = actorId is null ? null : await Participant.Get(actorId, ct);
            var tangent = await Community.Tangent.Get(tangentKey, ct);
            if (tangent is null || participant?.IsSuspended == true) return new TangentChannelDirectory([], page, null);
            var tangentMembership = actorId is null ? null : await TangentMembership.Get(TangentMembership.Key(tangentKey, actorId), ct);
            var topics = await Topic.QueryWithCount(topic => topic.TangentKey == tangentKey,
                directoryQuery.WithPagination(page, TopicConstants.PageSize), ct);
            var now = clock.GetUtcNow();
            var descriptions = new List<TopicDescription>(topics.Items.Count);
            foreach (var topic in topics.Items)
            {
                var membership = actorId is null ? null : await TopicMembership.Get(TopicMembership.Key(topic.Id, actorId), ct);
                var restriction = actorId is null ? null : await Restrictions.ForTopic(actorId, topic.Id, topic.TangentKey, now, ct);
                var policy = await Project(topic, tangent, topic.CurrentPolicy(space, actorId, membership, false, tangent, tangentMembership,
                    participant?.Classification ?? ParticipantClassification.Undeclared, restriction), participant, restriction, ct);
                if (policy.CanRead || policy.CanManage) descriptions.Add(TopicDescription.From(topic, policy));
            }
            await EntityContext.Commit(ct);
            return new TangentChannelDirectory(descriptions, page, topics.HasNextPage ? page + 1 : null);
        }
        finally { gate.Exit(); }
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
            if (denial is null) ActivityJournal.SignalAfterCommit();
            return new TopicAdministrationResult(audit.Accepted, audit.Denial, audit.Reason, "",
                audit.SpacePolicyRevision, audit.Id);
        }
        finally { gate.Exit(); }
    }

    public Task<TopicAdministrationResult> SetMembership(string actorId, string roomKey, string targetIdentifier, TopicRole role, CancellationToken ct)
        => Administer(actorId, roomKey, targetIdentifier, TopicAdministration.SetMembership, role,
            (space, topic, actor, targetId, target, tangent, tangentMembership, now) =>
            {
                var current = RequireTopic(topic);
                return new Change(current, current.ChangeMembership(space, actorId, actor, targetId!, target, role, now, tangent, tangentMembership, authorized: true));
            }, ct);

    public Task<TopicAdministrationResult> SetTopic(string actorId, string roomKey, string description, CancellationToken ct)
        => Administer(actorId, roomKey, null, TopicAdministration.SetTopic, null,
            (space, topic, actor, _, _, tangent, tangentMembership, now) =>
            {
                var current = RequireTopic(topic);
                current.ChangeDescription(space, actorId, actor, description, now, tangent, tangentMembership, authorized: true);
                return new Change(current);
            }, ct);

    public Task<TopicAdministrationResult> SetAdmission(string actorId, string roomKey, TopicAdmission admission, CancellationToken ct)
        => Administer(actorId, roomKey, null, TopicAdministration.SetAdmission, null,
            (space, topic, _, _, _, tangent, _, now) =>
            {
                var current = RequireTopic(topic);
                current.ChangeAdmission(space, actorId, admission, now, tangent, authorized: true);
                return new Change(current);
            }, ct);

    public Task<TopicAdministrationResult> SetReadAudience(string actorId, string roomKey, TopicReadAudience audience,
        bool publishExistingHistory, CancellationToken ct)
        => Administer(actorId, roomKey, null, TopicAdministration.SetReadAudience, null,
            (space, topic, _, _, _, tangent, _, now) =>
            {
                var current = RequireTopic(topic);
                current.ChangeReadAudience(space, actorId, audience, publishExistingHistory, now, tangent, authorized: true);
                return new Change(current);
            }, ct, audience, publishExistingHistory);

    public Task<TopicAdministrationResult> SetSettings(string actorId, string roomKey, bool allowPostEditing, bool isLocked,
        string? title, string? description, CancellationToken ct)
        => Administer(actorId, roomKey, null, TopicAdministration.SetSettings, null,
            (space, topic, actor, _, _, tangent, tangentMembership, now) =>
            {
                var current = RequireTopic(topic);
                current.ChangeSettings(space, actorId, actor, allowPostEditing, isLocked, title, description, now, tangent, tangentMembership, authorized: true);
                return new Change(current);
            }, ct);

    public async Task<AccessMapView> GetAccess(string actorId, string roomKey, string? expectedTangentKey, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var topic = await Topic.Get(roomKey, ct)
                ?? throw new TopicRuleViolation(TopicDenial.NotFound, "The requested Topic does not exist.");
            if (expectedTangentKey is not null && topic.TangentKey != expectedTangentKey)
                throw new TopicRuleViolation(TopicDenial.NotFound, "The requested Topic does not exist in this Tangent.");
            var participant = await Participant.Get(actorId, ct);
            var membership = await TopicMembership.Get(TopicMembership.Key(roomKey, actorId), ct);
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
            await EntityContext.Commit(ct);
            return new AccessMapView(selected, selected.ResolveTopic(parent), inherited, parent.ResolveTangent());
        }
        finally { gate.Exit(); }
    }

    public Task<TopicAdministrationResult> SetAccess(string actorId, string roomKey, AccessMap access, CancellationToken ct)
        => Administer(actorId, roomKey, null, TopicAdministration.SetAccess, null,
            (space, topic, actor, _, _, tangent, tangentMembership, now) =>
            {
                var current = RequireTopic(topic);
                current.ChangeAccess(space, actorId, actor, access, now, tangent, tangentMembership, authorized: true);
                return new Change(current);
            }, ct);

    /// <summary>
    /// Reload current authority and execute bounded local work under the same gate and transaction as administration.
    /// Do not call PDS/network services, nest a transaction, or reenter TopicGovernance/Arrival inside the callback.
    /// A denied snapshot still carries the selected revisions.
    /// </summary>
    public async Task<T> WithCurrentPolicy<T>(string? actorId, string roomKey,
        Func<TopicPolicy, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var topic = await Topic.Get(roomKey, ct);
            var membership = topic is null || actorId is null ? null
                : await TopicMembership.Get(TopicMembership.Key(roomKey, actorId), ct);
            var participant = actorId is null ? null : await Participant.Get(actorId, ct);
            var tangent = topic is null ? null : await Community.Tangent.Get(topic.TangentKey, ct);
            var tangentMembership = actorId is null || tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
            var restriction = topic is null || actorId is null ? null
                : await Restrictions.ForTopic(actorId, topic.Id, topic.TangentKey, clock.GetUtcNow(), ct);
            var policy = topic is null ? null : await Project(topic, tangent, topic.CurrentPolicy(space, actorId, membership, participant?.IsSuspended == true, tangent, tangentMembership,
                participant?.Classification ?? ParticipantClassification.Undeclared, restriction), participant, restriction, ct);
            policy ??= new TopicPolicy(roomKey, actorId, 0, space?.PolicyRevision ?? 0, TopicAdmission.InvitationOnly,
                    null, false, false, false, false, false, "room-not-found");
            var result = await operation(policy, ct);
            await EntityContext.Commit(ct);
            return result;
        }
        finally { gate.Exit(); }
    }

    private async Task<TopicAdministrationResult> Administer(string actorId, string roomKey, string? targetIdentifier,
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
                Topic.CheckKey(roomKey);
                topic = await Topic.Get(roomKey, ct);
                var actorParticipant = await Participant.Get(actorId, ct);
                if (actorParticipant?.IsSuspended == true)
                    throw new TopicRuleViolation(TopicDenial.Forbidden, "A suspended participant cannot administer topics.");
                // Scoped restrictions apply to administration as well as conversation: a banned or timed-out
                // participant cannot administer here. Owners are never restriction targets.
                if (topic is not null && await Restrictions.ForTopic(actorId, topic.Id, topic.TangentKey, now, ct) is not null)
                    throw new TopicRuleViolation(TopicDenial.Forbidden, "A scoped restriction currently denies this administration.");
                var actor = topic is null ? null : await TopicMembership.Get(TopicMembership.Key(roomKey, actorId), ct);
                var target = topic is null || targetId is null ? null
                    : await TopicMembership.Get(TopicMembership.Key(roomKey, targetId), ct);
                var tangent = topic is null ? null : await Community.Tangent.Get(topic.TangentKey, ct);
                var tangentMembership = tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
                if (topic is not null)
                {
                    var restriction = await Restrictions.ForTopic(actorId, topic.Id, topic.TangentKey, now, ct);
                    var projected = await Project(topic, tangent,
                        topic.CurrentPolicy(space, actorId, actor, false, tangent, tangentMembership,
                            actorParticipant?.Classification ?? ParticipantClassification.Undeclared, restriction),
                        actorParticipant, restriction, ct);
                    var allowed = operation switch
                    {
                        TopicAdministration.SetMembership => projected.CanManage,
                        TopicAdministration.SetAdmission or TopicAdministration.SetReadAudience => projected.CanAppointManagers,
                        _ => projected.CanManage
                    };
                    if (!allowed) throw new TopicRuleViolation(TopicDenial.Forbidden, "The current role does not permit this Topic operation.");
                }
                // The domain command, membership row and audit use the same once-resolved identity.
                change = apply(space, topic, actor, targetId, target, tangent, tangentMembership, now);
            }
            catch (TopicRuleViolation rejected) { denial = rejected; }

            // Policy errors are ordinary audited denials. Persistence failures propagate and roll back everything.
            if (change is not null)
            {
                topic = change.Topic;
                await topic.Save(ct);
                if (change.Membership is not null) await change.Membership.Save(ct);
                var kind = operation == TopicAdministration.SetMembership ? ActivityKind.MembershipChanged : ActivityKind.RoomChanged;
                await ActivityJournal.AppendInTransaction(kind, topic.Id, actorId, change.Membership?.ParticipantId, topic.TangentKey, ct: ct);
            }
            var audit = new TopicAudit
            {
                ActorParticipantId = actorId, RoomKey = roomKey, TargetParticipantId = targetId, Operation = operation,
                RequestedRole = requestedRole, RequestedReadAudience = requestedReadAudience,
                PublishExistingHistory = publishExistingHistory, Accepted = denial is null, Denial = denial?.Denial,
                Reason = denial?.Message ?? "Accepted.", SelectedPolicyRevision = topic?.PolicyRevision ?? 0,
                SpacePolicyRevision = space?.PolicyRevision ?? 0, OccurredAt = now
            };
            await audit.Save(ct);
            if (audit.Accepted)
                await CommandCommit.Report(new TopicAdministrationResult(true, null, audit.Reason, roomKey,
                    audit.SelectedPolicyRevision, audit.Id), ct);
            await EntityContext.Commit(ct);
            if (change is not null) ActivityJournal.SignalAfterCommit();
            result = new TopicAdministrationResult(audit.Accepted, audit.Denial, audit.Reason, roomKey,
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
