using CarpaNet.Identity;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Tangent.Activity;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Stewardship;
using Tangent.Spaces;
using Tangent.Access;

namespace Tangent.Community;

/// <summary>Coordinates card ownership, Tangent membership and topic creation under the host policy gate.</summary>
public sealed class TangentGovernance(TimeProvider clock, PolicyGate gate,
    ParticipantDirectory directory, TangentRoleAccess roleAccess)
{
    public const int TangentsPerPage = 50;
    public const int TopicsPerTangent = 100;
    public const int MaximumPage = 10000;
    // The prototype deliberately caps policy work. A scan-limit flag says the caller
    // did not receive a complete directory; it never reports a private count.
    public const int MaximumDirectoryScan = 500;
    private static readonly QueryDefinition tangents = new() { Sort = SortSpecParser.ParseStrict<Tangent>(nameof(Community.Tangent.Id)), CountStrategy = CountStrategy.Exact };
    private static readonly QueryDefinition topics = new() { Sort = SortSpecParser.ParseStrict<Topic>(nameof(Topic.Id)), CountStrategy = CountStrategy.Exact };

    public Task<TangentsResponse> List(string? actorId, CancellationToken ct) => List(actorId, 1, 1, ct);

    public Task<TangentsResponse> List(string? actorId, int page, CancellationToken ct) => List(actorId, page, 1, ct);

    /// <summary>Returns one actor-filtered Tangent card without using directory paging as an authorization oracle.</summary>
    public async Task<TangentDescription?> Describe(string? actorId, string key, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var tangent = await Community.Tangent.Get(key, ct);
            if (tangent is null) return null;
            var participant = actorId is null ? null : await Participant.Get(actorId, ct);
            var membership = actorId is null ? null : await TangentMembership.Get(TangentMembership.Key(key, actorId), ct);
            if (participant?.IsSuspended == true) return null;
            var bag = await roleAccess.Bag(actorId, ct);
            var effective = (tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent();
            var owner = tangent.IsOwner(actorId) || space?.IsOwner(actorId) == true;
            if (!owner && !TangentRoleAccess.CanDo(effective.See, bag)) return null;
            var restricted = actorId is not null && !owner && await Restrictions.ForTangent(actorId, tangent.Id, clock.GetUtcNow(), ct) is not null;
            var canManage = !restricted && (owner || TangentRoleAccess.CanDo(effective.Manage, bag));
            var canCreateTopic = !restricted && (owner || TangentRoleAccess.CanDo(effective.CreateTopics, bag))
                && tangent.ParticipationRights(participant?.Classification ?? ParticipantClassification.Undeclared).Write;
            var topics = await VisibleTopics(space, actorId, participant, tangent.Id, tangent, membership, 1, includeManage: true, ct);
            var pending = actorId is not null && membership is null
                && await TangentJoinRequest.Get(TangentJoinRequest.Key(tangent.Id, actorId), ct) is { Pending: true };
            var result = new TangentDescription(tangent.Id, tangent.Name, tangent.Description, tangent.Motto, tangent.Accent, tangent.Artwork,
                tangent.OwnerParticipantId, owner, canManage, topics.Items, topics.NextPage is not null, topics.NextPage, topics.ScanLimited,
                owner || bag.Contains(TangentBuiltInRoles.Member.Token), membership?.Role,
                tangent.EffectiveAdmission, pending, canCreateTopic,
                new PermissionView(owner ? "owner" : membership?.Role.ToString().ToLowerInvariant() ?? "visitor", "tangent",
                    canManage ? ["read", "createTopic", "manageTangent", "manageParticipants"] : canCreateTopic ? ["read", "createTopic"] : ["read"], new Dictionary<string, string>()));
            await EntityContext.Commit(ct);
            return result;
        }
        finally { gate.Exit(); }
    }

    public async Task<TangentsResponse> List(string? actorId, int page, int topicPage, CancellationToken ct)
    {
        CheckPage(page); CheckPage(topicPage);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var participant = actorId is null ? null : await Participant.Get(actorId, ct);
            var bag = await roleAccess.Bag(actorId, ct);
            if (participant?.IsSuspended == true)
            {
                await EntityContext.Commit(ct);
                return new TangentsResponse([], false, page);
            }
            var selected = await VisibleTangents(space, actorId, page, ct);
            var visible = new List<TangentDescription>(selected.Items.Count);
            foreach (var tangent in selected.Items)
            {
                var membership = actorId is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
                var owner = tangent.IsOwner(actorId) || space?.IsOwner(actorId) == true;
                var restricted = actorId is not null && !owner && await Restrictions.ForTangent(actorId, tangent.Id, clock.GetUtcNow(), ct) is not null;
                var effective = (tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent();
                var canManage = !restricted && (owner || TangentRoleAccess.CanDo(effective.Manage, bag));
                var canCreateTopic = !restricted && (owner || TangentRoleAccess.CanDo(effective.CreateTopics, bag))
                    && tangent.ParticipationRights(participant?.Classification ?? ParticipantClassification.Undeclared).Write;
                var isMember = owner || bag.Contains(TangentBuiltInRoles.Member.Token);
                var pending = !isMember && actorId is not null
                    && await TangentJoinRequest.Get(TangentJoinRequest.Key(tangent.Id, actorId), ct) is { Pending: true };
                var topics = await VisibleTopics(space, actorId, participant, tangent.Id, tangent, membership, topicPage, includeManage: true, ct);
                visible.Add(new TangentDescription(tangent.Id, tangent.Name, tangent.Description, tangent.Motto, tangent.Accent, tangent.Artwork,
                    tangent.OwnerParticipantId, owner, canManage, topics.Items, topics.NextPage is not null, topics.NextPage, topics.ScanLimited,
                    isMember, membership?.Role, tangent.EffectiveAdmission, pending, canCreateTopic,
                    new PermissionView(owner ? "owner" : membership?.Role.ToString().ToLowerInvariant() ?? "visitor", "tangent",
                        canManage ? new[] { "read", "createTopic", "manageTangent", "manageParticipants" } : canCreateTopic ? new[] { "read", "createTopic" } : new[] { "read" }, new Dictionary<string, string>())));
            }
            await EntityContext.Commit(ct);
            return new TangentsResponse(visible, await CanCreateTangent(space, participant, ct), page, selected.NextPage, selected.ScanLimited);
        }
        finally { gate.Exit(); }
    }

    /// <summary>Bound, authorized directory for activity and topic continuation; page metadata never counts hidden topics.</summary>
    public async Task<TangentTopicDirectory> ListAuthorizedTopics(string did, int page, CancellationToken ct)
    {
        CheckPage(page);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var participant = await Participant.Get(did, ct);
            var result = participant?.IsSuspended == true
                ? new TopicSlice([], null)
                : await VisibleTopics(space, did, participant, null, null, null, page, includeManage: false, ct);
            await EntityContext.Commit(ct);
            return new TangentTopicDirectory(result.Items, page, result.NextPage, result.ScanLimited);
        }
        finally { gate.Exit(); }
    }

    public async Task<TangentDescription> Create(string actorId, string key, string name, string? description, string? motto,
        string? accent, string? artwork, CancellationToken ct, TangentAdmission? admission = null)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var actor = await Participant.Get(actorId, ct);
            if (actor?.IsSuspended == true) throw new TangentRuleViolation(TangentDenial.Forbidden, "A suspended participant cannot create a Tangent.");
            if (space is null || !await CanCreateTangent(space, actor, ct))
                throw new TangentRuleViolation(TangentDenial.Forbidden, "Server access does not permit this participant to create a Tangent.");
            if (await Community.Tangent.Get(key, ct) is not null)
                throw new TangentRuleViolation(TangentDenial.AlreadyExists, "That Tangent key already exists.");
            var tangent = space.IsOwner(actorId) || actor?.Classification == ParticipantClassification.Human || space.AllowAgentTangentOwnership
                ? Community.Tangent.CreateForAuthorizedActor(space, actorId, space.OwnerParticipantId, key, name, description, motto, accent, artwork, clock.GetUtcNow())
                : Community.Tangent.CreateForAuthorizedActor(space, space.OwnerParticipantId, space.OwnerParticipantId, key, name, description, motto, accent, artwork, clock.GetUtcNow());
            if (admission is { } initialAdmission)
                tangent.ChangeParticipationPolicy(tangent.OwnerParticipantId, initialAdmission, ParticipationPreset.Everyone, UndeclaredAccess.Write, clock.GetUtcNow());
            await tangent.Save(ct);
            if (!tangent.IsOwner(actorId)) await TangentMembership.Assign(tangent, actorId, TangentRole.Admin, space.OwnerParticipantId, clock.GetUtcNow()).Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.TangentChanged, "", actorId, tangentKey: tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return await DescribeForOwner(tangent, actorId, ct);
        }
        finally { gate.Exit(); }
    }

    // One durable transition; a lost response can be retried without creating another card.
    public async Task<TangentDescription> CompleteOnboarding(string actorId, string? name, string? description,
        bool skip, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var actor = await Participant.Get(actorId, ct);
            if (space?.IsOwner(actorId) != true || actor is null || actor.IsSuspended)
                throw new UnauthorizedAccessException();
            var home = await Community.Tangent.Get(Community.Tangent.HomeKey, ct)
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The home Tangent does not exist.");
            if (!home.SetupComplete)
            {
                home.Change(actorId, skip ? "My Tangent" : name ?? "", skip ? "" : description,
                    "Make room for the next thought.", "#f4b942", "", clock.GetUtcNow());
                await home.Save(ct);
                await ActivityJournal.AppendInTransaction(ActivityKind.TangentChanged, "", actorId, tangentKey: home.Id, ct: ct);
            }
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return await DescribeForOwner(home, actorId, ct);
        }
        finally { gate.Exit(); }
    }

    public async Task<TangentDescription> Change(string actorId, string key, string? name, string? description, string? motto,
        string? accent, string? artwork, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var tangent = await Community.Tangent.Get(key, ct) ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorId, ct);
            if (actor?.IsSuspended == true) throw new TangentRuleViolation(TangentDenial.Forbidden, "A suspended participant cannot manage a Tangent.");
            var bag = await roleAccess.Bag(actorId, ct);
            var canManage = (tangent.IsOwner(actorId) || TangentRoleAccess.CanDo(
                (tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent().Manage, bag))
                && await Restrictions.ForTangent(actorId, tangent.Id, clock.GetUtcNow(), ct) is null;
            tangent.ChangeAuthorized(actorId, canManage, name, description, motto, accent, artwork, clock.GetUtcNow());
            await tangent.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.TangentChanged, "", actorId, tangentKey: tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return await DescribeForOwner(tangent, actorId, ct);
        }
        finally { gate.Exit(); }
    }

    public async Task<AccessMapView> GetAccess(string actorId, string key, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            var tangent = await Community.Tangent.Get(key, ct)
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var bag = await roleAccess.Bag(actorId, ct);
            var canManage = (tangent.IsOwner(actorId) || TangentRoleAccess.CanDo(
                (tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent().Manage, bag))
                && await Restrictions.ForTangent(actorId, key, clock.GetUtcNow(), ct) is null;
            if (!canManage) throw new TangentRuleViolation(TangentDenial.Forbidden, "Only a Tangent administrator can inspect its access settings.");
            var selected = (tangent.Access ?? AccessMap.TangentDefaults()).NormalizeForTangent();
            await EntityContext.Commit(ct);
            return new AccessMapView(selected, selected.ResolveTangent(), []);
        }
        finally { gate.Exit(); }
    }

    public async Task<AccessMapView> SetAccess(string actorId, string key, AccessMap access, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var tangent = await Community.Tangent.Get(key, ct)
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorId, ct);
            var bag = await roleAccess.Bag(actorId, ct);
            var canManage = actor?.IsSuspended != true
                && (tangent.IsOwner(actorId) || TangentRoleAccess.CanDo(
                    (tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent().Manage, bag))
                && await Restrictions.ForTangent(actorId, key, clock.GetUtcNow(), ct) is null;
            tangent.ChangeAccess(actorId, access, clock.GetUtcNow(), canManage);
            await tangent.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.TangentChanged, "", actorId, tangentKey: key, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            var selected = tangent.Access!.NormalizeForTangent();
            return new AccessMapView(selected, selected.ResolveTangent(), []);
        }
        finally { gate.Exit(); }
    }

    public async Task<TangentTopicCreation> CreateTopic(string actorId, string tangentKey, string roomKey, string title,
        TopicAdmission admission, string? description, CancellationToken ct, bool membersOnly = false)
    {
        TangentTopicCreation result;
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var tangent = await Community.Tangent.Get(tangentKey, ct) ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorId, ct);
            var actorMembership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
            var bag = await roleAccess.Bag(actorId, ct);
            var canCreate = TangentRoleAccess.CanDo(
                (tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent().CreateTopics, bag);
            if (actor?.IsSuspended == true || !tangent.ParticipationRights(actor?.Classification ?? ParticipantClassification.Undeclared).Write
                || !(tangent.IsOwner(actorId) || canCreate))
                throw new TangentRuleViolation(TangentDenial.Forbidden, "The current role cannot create Topics in this Tangent.");
            var now = clock.GetUtcNow();
            // Scoped restrictions apply to administration as well: a banned or timed-out delegated administrator
            // cannot create topics. The owner is never a restriction target.
            if (!tangent.IsOwner(actorId) && await Restrictions.ForTangent(actorId, tangent.Id, now, ct) is not null)
                throw new TangentRuleViolation(TangentDenial.Forbidden, "A scoped restriction currently denies topic administration.");
            if (await Topic.Get(roomKey, ct) is not null) throw new TangentRuleViolation(TangentDenial.AlreadyExists, "That stable topic key already exists.");
            var topic = tangent.IsOwner(actorId)
                ? Topic.Create(space, actorId, roomKey, title, admission, now, tangent)
                : Topic.CreateDelegated(space, actorId, roomKey, title, admission, now, tangent);
            topic.MembersOnly = membersOnly;
            if (description is not null) topic.ChangeDescription(space, actorId, null, description, now, tangent, actorMembership);
            await topic.Save(ct);
            var audit = new TopicAudit { ActorParticipantId = actorId, TopicKey = roomKey, Operation = TopicAdministration.Create, Accepted = true,
                Reason = "Accepted.", SelectedPolicyRevision = topic.PolicyRevision, SpacePolicyRevision = space?.PolicyRevision ?? 0, OccurredAt = now };
            await audit.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.TopicChanged, topic.Id, actorId, tangentKey: tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            var policy = topic.CurrentPolicy(space, actorId, null, actor?.IsSuspended == true, tangent, actorMembership,
                actor?.Classification ?? ParticipantClassification.Undeclared);
            result = new TangentTopicCreation(new TopicAdministrationResult(true, null, "Accepted.", topic.Id, topic.PolicyRevision, audit.Id), TopicDescription.From(topic, policy));
        }
        finally { gate.Exit(); }
        return result;
    }

    /// <summary>Invitation workflows can call this now; the human invitation endpoint remains a later scope.
    /// The target arrives as an external identifier (DID, internal DID, or handle) and resolves to
    /// its current holder here.</summary>
    public async Task<TangentMembershipResult> SetMembership(string actorId, string tangentKey, string targetIdentifier, TangentRole role, CancellationToken ct)
    {
        TangentMembershipResult result;
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var tangent = await Community.Tangent.Get(tangentKey, ct) ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorId, ct);
            var target = (await directory.ByIdentifier(targetIdentifier, ct))?.Participant
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The participant must first establish a verified arrival.");
            if (actor?.IsSuspended == true || !tangent.IsOwner(actorId)) throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the current Tangent owner can change Tangent membership.");
            if (tangent.IsOwner(target.Id) || target.Id == actorId) throw new TangentRuleViolation(TangentDenial.InvalidInput, "Choose a non-owner participant for membership.");
            if (!Enum.IsDefined(role)) throw new TangentRuleViolation(TangentDenial.InvalidInput, "Choose member or removed.");
            var member = TangentMembership.Assign(tangent, target.Id, role, actorId, clock.GetUtcNow());
            await member.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.MembershipChanged, "", actorId, target.Id, tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            result = new TangentMembershipResult(member.TangentKey, member.ParticipantId, member.Role);
        }
        finally { gate.Exit(); }
        return result;
    }

    /// <summary>Current card access for global activity entries that have no topic key.</summary>
    public async Task<bool> CanAccess(string did, string tangentKey, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var participant = await Participant.Get(did, ct);
            var tangent = await Community.Tangent.Get(tangentKey, ct);
            var bag = await roleAccess.Bag(did, ct);
            var allowed = space is not null && participant?.IsSuspended != true && tangent is not null
                && await Restrictions.ForTangent(did, tangentKey, clock.GetUtcNow(), ct) is null
                && tangent.ParticipationRights(participant?.Classification ?? ParticipantClassification.Undeclared).Read
                && (tangent.IsOwner(did) || TangentRoleAccess.CanDo(
                    (tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent().See, bag));
            await EntityContext.Commit(ct);
            return allowed;
        }
        finally { gate.Exit(); }
    }

    private async Task<TangentDescription> DescribeForOwner(Tangent tangent, string actorId, CancellationToken ct)
    {
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var participant = await Participant.Get(actorId, ct);
        var membership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
        var owner = tangent.IsOwner(actorId) || space?.IsOwner(actorId) == true;
        var manage = owner || membership?.Role == TangentRole.Admin;
        var topics = await VisibleTopics(space, actorId, participant, tangent.Id, tangent, membership, 1, includeManage: true, ct);
        return new TangentDescription(tangent.Id, tangent.Name, tangent.Description, tangent.Motto, tangent.Accent, tangent.Artwork,
            tangent.OwnerParticipantId, owner, manage, topics.Items, topics.NextPage is not null, topics.NextPage, topics.ScanLimited,
            IsMember: true, MembershipRole: membership?.Role, Admission: tangent.EffectiveAdmission,
            CanCreateTopic: manage,
            Permissions: new PermissionView(owner ? "owner" : "admin", "tangent",
                manage ? ["read", "createTopic", "manageTangent", "manageParticipants"] : ["read"], new Dictionary<string,string>()));
    }

    private async Task<TangentSlice> VisibleTangents(Space? space, string? did, int page, CancellationToken ct)
    {
        if (space is null) return new TangentSlice([], null);
        var bag = await roleAccess.Bag(did, ct);
        var skipped = checked((page - 1) * TangentsPerPage);
        var output = new List<Tangent>(TangentsPerPage + 1);
        var visible = 0;
        var scanned = 0;
        for (var storagePage = 1; ; storagePage++)
        {
            var candidates = await Community.Tangent.AllWithCount(tangents.WithPagination(storagePage, TangentsPerPage), ct);
            foreach (var tangent in candidates.Items)
            {
                if (++scanned > MaximumDirectoryScan) return new TangentSlice(output, null, true);
                if (!tangent.IsOwner(did)
                    && !TangentRoleAccess.CanDo((tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent().See, bag)) continue;
                if (visible++ < skipped) continue;
                output.Add(tangent);
                if (output.Count > TangentsPerPage)
                    return new TangentSlice(output.Take(TangentsPerPage).ToArray(), page + 1, false);
            }
            if (!candidates.HasNextPage) return new TangentSlice(output, null, false);
        }
    }

    private async Task<bool> CanCreateTangent(Space? space, Participant? actor, CancellationToken ct)
    {
        if (space is null || actor is null || actor.IsSuspended) return false;
        var bag = await roleAccess.Bag(actor.Id, ct);
        return TangentRoleAccess.CanDo((space.Access ?? AccessMap.ServerDefaults()).ResolveServer().CreateTangents, bag);
    }

    private async Task<TopicSlice> VisibleTopics(Space? space, string? did, Participant? participant, string? tangentKey,
        Tangent? selectedTangent, TangentMembership? selectedMembership, int page, bool includeManage, CancellationToken ct)
    {
        if (space is null || participant?.IsSuspended == true) return new TopicSlice([], null);
        var skipped = checked((page - 1) * TopicsPerTangent);
        var output = new List<TopicDescription>(TopicsPerTangent + 1);
        var visible = 0;
        var scanned = 0;
        var tangentCache = new Dictionary<string, Tangent?>();
        var membershipCache = new Dictionary<string, TangentMembership?>();
        var now = clock.GetUtcNow();
        for (var storagePage = 1; ; storagePage++)
        {
            var candidates = tangentKey is null
                ? await Topic.Query(topic => true, topics.WithPagination(storagePage, TopicsPerTangent), ct)
                : await Topic.Query(topic => topic.TangentKey == tangentKey, topics.WithPagination(storagePage, TopicsPerTangent), ct);
            foreach (var topic in candidates)
            {
                if (++scanned > MaximumDirectoryScan) return new TopicSlice(output, null, true);
                var tangent = selectedTangent;
                var membership = selectedMembership;
                if (tangent is null)
                {
                    if (!tangentCache.TryGetValue(topic.TangentKey, out tangent))
                    {
                        tangent = await Community.Tangent.Get(topic.TangentKey, ct);
                        tangentCache[topic.TangentKey] = tangent;
                    }
                    if (did is not null && !membershipCache.TryGetValue(topic.TangentKey, out membership))
                    {
                        membership = tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(topic.TangentKey, did), ct);
                        membershipCache[topic.TangentKey] = membership;
                    }
                }
                var roomMembership = did is null ? null : await TopicMembership.Get(TopicMembership.Key(topic.Id, did), ct);
                // A scoped denial scrubs the topic from this directory; the topic record is checked
                // before its Tangent's inherited record.
                var restriction = did is null ? null
                    : await Restrictions.ForTopic(did, topic.Id, topic.TangentKey, now, ct);
                var policy = await roleAccess.ProjectTopic(topic, tangent,
                    topic.CurrentPolicy(space, did, roomMembership, participant?.IsSuspended == true, tangent, membership,
                        participant?.Classification ?? ParticipantClassification.Undeclared, restriction),
                    participant?.IsSuspended == true, restriction,
                    participant?.Classification ?? ParticipantClassification.Undeclared, ct);
                if (!policy.CanRead && !(includeManage && policy.CanManage)) continue;
                if (visible++ < skipped) continue;
                output.Add(TopicDescription.From(topic, policy));
                if (output.Count > TopicsPerTangent)
                    return new TopicSlice(output.Take(TopicsPerTangent).ToArray(), page + 1, false);
            }
            if (candidates.Count < TopicsPerTangent) return new TopicSlice(output, null, false);
        }
    }

    private static void CheckPage(int page)
    {
        if (page is < 1 or > MaximumPage)
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "Choose a directory page between 1 and 10000.");
    }

    private sealed record TangentSlice(IReadOnlyList<Tangent> Items, int? NextPage, bool ScanLimited = false);
    private sealed record TopicSlice(IReadOnlyList<TopicDescription> Items, int? NextPage, bool ScanLimited = false);
}
