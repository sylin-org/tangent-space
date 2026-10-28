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
    ParticipantDirectory directory, TangentRoleAccess roleAccess, SignalBus signals)
{
    public const int TangentsPerPage = 50;
    public const int TopicsPerTangent = 100;
    public const int MaximumPage = 10000;
    // The prototype deliberately caps policy work. A scan-limit flag says the caller
    // did not receive a complete directory; it never reports a private count.
    public const int MaximumDirectoryScan = 500;
    private static readonly QueryDefinition tangents = new() { Sort = SortSpecParser.ParseStrict<Tangent>(nameof(Community.Tangent.Id)), CountStrategy = CountStrategy.Exact };
    private static readonly QueryDefinition topics = new() { Sort = SortSpecParser.ParseStrict<Topic>(nameof(Topic.Id)), CountStrategy = CountStrategy.Exact };
    private static readonly QueryDefinition articles = new() { Sort = SortSpecParser.ParseStrict<Topic>("-CreatedAt,-Id"), CountStrategy = CountStrategy.Exact };

    public Task<TangentsResponse> List(string? actorId, CancellationToken ct) => List(actorId, 1, 1, ct);

    public Task<TangentsResponse> List(string? actorId, int page, CancellationToken ct) => List(actorId, page, 1, ct);

    /// <summary>Returns one actor-filtered Tangent card without using directory paging as an authorization oracle.</summary>
    public async Task<TangentDescription?> Describe(string? actorId, string key, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var tangent = await Community.Tangent.Get(key, ct);
        if (tangent is null) return null;
        var participant = actorId is null ? null : await Participant.Get(actorId, ct);
        var membership = actorId is null ? null : await TangentMembership.Get(TangentMembership.Key(key, actorId), ct);
        if (participant?.IsSuspended == true) return null;
        var bag = TangentRoleAccess.InTangent(await roleAccess.Bag(actorId, ct), membership);
        var effective = (tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent();
        var owner = tangent.IsOwner(actorId) || space?.IsOwner(actorId) == true;
        var restriction = actorId is not null && !owner ? await Restrictions.ForTangent(actorId, tangent.Id, clock.GetUtcNow(), ct) : null;
        var restricted = restriction is not null;
        if (restriction is { Banned: true }) return null;
        if (!owner && (membership?.Role != TangentRole.Admin || restricted)
            && !TangentRoleAccess.CanDo(effective.See, bag)) return null;
        var admitted = await SpaceAdmission.Admitted(space, actorId, ct) && IsAdmitted(space, tangent, actorId, membership);
        var canManage = admitted && !restricted && HoldsManage(space, tangent, actorId, membership, bag);
        var canCreateTopic = admitted && restriction is not { BlockStarting: true } && (owner || TangentRoleAccess.CanDo(effective.CreateTopics, bag))
            && tangent.ParticipationRights(participant?.Classification ?? ParticipantClassification.Undeclared).Write;
        var topics = await VisibleTopics(space, actorId, participant, tangent.Id, tangent, membership, 1, includeManage: true, ct);
        var pending = actorId is not null && membership is null
            && await TangentJoinRequest.Get(TangentJoinRequest.Key(tangent.Id, actorId), ct) is { Pending: true };
        var result = new TangentDescription(tangent.Id, tangent.Name, tangent.Description, tangent.Motto, tangent.Accent, tangent.Artwork,
            tangent.OwnerParticipantId, owner, canManage, topics.Items, topics.NextPage is not null, topics.NextPage, topics.ScanLimited,
            owner || membership?.Role is TangentRole.Admin or TangentRole.Member or TangentRole.Reader, membership?.Role,
            tangent.EffectiveAdmission, pending, canCreateTopic,
            new PermissionView(owner ? "owner" : membership?.Role.ToString().ToLowerInvariant() ?? "visitor", "tangent",
                canManage ? ["read", "createTopic", "manageTangent", "manageParticipants"] : canCreateTopic ? ["read", "createTopic"] : ["read"], new Dictionary<string, string>()),
            CanManageAdmission: tangent.IsOwner(actorId), Kind: tangent.Kind, ReadAudience: AudienceOf(tangent));
        return result;
    }

    public async Task<TangentsResponse> List(string? actorId, int page, int topicPage, CancellationToken ct)
    {
        CheckPage(page); CheckPage(topicPage);
        using var fresh = EntityContext.NoCache();
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var participant = actorId is null ? null : await Participant.Get(actorId, ct);
        var bag = await roleAccess.Bag(actorId, ct);
        if (participant?.IsSuspended == true)
        {
            return new TangentsResponse([], false, page);
        }
        var selected = await VisibleTangents(space, actorId, page, ct);
        var visible = new List<TangentDescription>(selected.Items.Count);
        foreach (var tangent in selected.Items)
        {
            var membership = actorId is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
            var owner = tangent.IsOwner(actorId) || space?.IsOwner(actorId) == true;
            var restriction = actorId is not null && !owner ? await Restrictions.ForTangent(actorId, tangent.Id, clock.GetUtcNow(), ct) : null;
        var restricted = restriction is not null;
            var scopedBag = TangentRoleAccess.InTangent(bag, membership);
            var effective = (tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent();
            var admitted = await SpaceAdmission.Admitted(space, actorId, ct) && IsAdmitted(space, tangent, actorId, membership);
            var canManage = admitted && !restricted && HoldsManage(space, tangent, actorId, membership, scopedBag);
            var canCreateTopic = admitted && restriction is not { BlockStarting: true } && (owner || TangentRoleAccess.CanDo(effective.CreateTopics, scopedBag))
                && tangent.ParticipationRights(participant?.Classification ?? ParticipantClassification.Undeclared).Write;
            var isMember = owner || membership?.Role is TangentRole.Admin or TangentRole.Member or TangentRole.Reader;
            var pending = !isMember && actorId is not null
                && await TangentJoinRequest.Get(TangentJoinRequest.Key(tangent.Id, actorId), ct) is { Pending: true };
            var topics = await VisibleTopics(space, actorId, participant, tangent.Id, tangent, membership, topicPage, includeManage: true, ct);
            visible.Add(new TangentDescription(tangent.Id, tangent.Name, tangent.Description, tangent.Motto, tangent.Accent, tangent.Artwork,
                tangent.OwnerParticipantId, owner, canManage, topics.Items, topics.NextPage is not null, topics.NextPage, topics.ScanLimited,
                isMember, membership?.Role, tangent.EffectiveAdmission, pending, canCreateTopic,
                new PermissionView(owner ? "owner" : membership?.Role.ToString().ToLowerInvariant() ?? "visitor", "tangent",
                    canManage ? new[] { "read", "createTopic", "manageTangent", "manageParticipants" } : canCreateTopic ? new[] { "read", "createTopic" } : new[] { "read" }, new Dictionary<string, string>()),
                CanManageAdmission: tangent.IsOwner(actorId), Kind: tangent.Kind, ReadAudience: AudienceOf(tangent)));
        }
        return new TangentsResponse(visible, await CanCreateTangent(space, participant, ct), page, selected.NextPage, selected.ScanLimited);
    }

    /// <summary>Bound, authorized directory for activity and topic continuation; page metadata never counts hidden topics.</summary>
    public async Task<TangentTopicDirectory> ListAuthorizedTopics(string did, int page, CancellationToken ct)
    {
        CheckPage(page);
        using var fresh = EntityContext.NoCache();
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var participant = await Participant.Get(did, ct);
        var result = participant?.IsSuspended == true
            ? new TopicSlice([], null)
            : await VisibleTopics(space, did, participant, null, null, null, page, includeManage: false, ct);
        return new TangentTopicDirectory(result.Items, page, result.NextPage, result.ScanLimited);
    }

    /// <summary>Readable Topics within the same bounded policy scan used by the directory.</summary>
    public async Task<SummaryTopicSlice> ReadableTopicsForSummary(string? actorId, string tangentKey, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var participant = actorId is null ? null : await Participant.Get(actorId, ct);
        var tangent = await Community.Tangent.Get(tangentKey, ct);
        if (tangent is null || participant?.IsSuspended == true)
            return new SummaryTopicSlice([], false);
        var membership = actorId is null ? null : await TangentMembership.Get(TangentMembership.Key(tangentKey, actorId), ct);
        var slice = await VisibleTopics(space, actorId, participant, tangentKey, tangent, membership,
            1, includeManage: false, ct, MaximumDirectoryScan);
        return new SummaryTopicSlice(slice.Items, slice.ScanLimited);
    }

    internal async Task<TangentTopicDirectory> ListTopicDirectory(string? actorId, string? tangentKey, int page, CancellationToken ct)
    {
        CheckPage(page);
        using var fresh = EntityContext.NoCache();
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var participant = actorId is null ? null : await Participant.Get(actorId, ct);
        var tangent = tangentKey is null ? null : await Community.Tangent.Get(tangentKey, ct);
        if (tangentKey is not null && tangent is null || participant?.IsSuspended == true)
            return new TangentTopicDirectory([], page, null);
        var membership = actorId is null || tangentKey is null ? null
            : await TangentMembership.Get(TangentMembership.Key(tangentKey, actorId), ct);
        var slice = await VisibleTopics(space, actorId, participant, tangentKey, tangent, membership,
            page, includeManage: true, ct);
        return new TangentTopicDirectory(slice.Items, page, slice.NextPage, slice.ScanLimited);
    }

    public async Task<TangentDescription> Create(string actorId, string key, string name, string? description, string? motto,
        string? accent, string? artwork, CancellationToken ct, TangentAdmission? admission = null, string? kind = null, string? readAudience = null)
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
                ? Community.Tangent.CreateForAuthorizedActor(space, actorId, space.OwnerParticipantId, key, name, description, motto, accent, artwork, clock.GetUtcNow(), kind)
                : Community.Tangent.CreateForAuthorizedActor(space, space.OwnerParticipantId, space.OwnerParticipantId, key, name, description, motto, accent, artwork, clock.GetUtcNow(), kind);
            if (readAudience is not null)
            {
                var token = readAudience switch { "Public" => AccessTokens.Everyone, "Admitted" => AccessTokens.TangentMember, _ => throw new ArgumentException("Choose Public or Admitted.") };
                tangent.ChangeAccess(new AccessMap { See = [token] }, clock.GetUtcNow());
            }
            if (admission is { } initialAdmission)
                tangent.ChangeParticipationPolicy(tangent.OwnerParticipantId, initialAdmission, ParticipationPreset.Everyone, UndeclaredAccess.Write, clock.GetUtcNow());
            await tangent.Save(ct);
            if (!tangent.IsOwner(actorId)) await TangentMembership.Assign(tangent, actorId, TangentRole.Admin, space.OwnerParticipantId, clock.GetUtcNow()).Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.TangentChanged, "", actorId, tangentKey: tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            signals.Pulse();
            return await DescribeForOwner(tangent, actorId, ct);
        }
        finally { gate.Exit(); }
    }

    public async Task<TangentDescription> Change(string actorId, string key, string? name, string? description, string? motto,
        string? accent, string? artwork, CancellationToken ct, string? kind = null)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var tangent = await Community.Tangent.Get(key, ct) ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorId, ct);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var membership = await TangentMembership.Get(TangentMembership.Key(key, actorId), ct);
            if (actor?.IsSuspended == true) throw new TangentRuleViolation(TangentDenial.Forbidden, "A suspended participant cannot manage a Tangent.");
            var bag = TangentRoleAccess.InTangent(await roleAccess.Bag(actorId, ct), membership);
            var canManage = await SpaceAdmission.Admitted(space, actorId, ct) && await SpaceAdmission.Admitted(space, actorId, ct) && HoldsManage(space, tangent, actorId, membership, bag)
                && await Restrictions.ForTangent(actorId, tangent.Id, clock.GetUtcNow(), ct) is null;
            if (!canManage) throw new TangentRuleViolation(TangentDenial.Forbidden, "Only a Tangent administrator can change its card.");
            tangent.ChangeAuthorized(name, description, motto, accent, artwork, clock.GetUtcNow(), kind);
            await tangent.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.TangentChanged, "", actorId, tangentKey: tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            signals.Pulse();
            return await DescribeForOwner(tangent, actorId, ct);
        }
        finally { gate.Exit(); }
    }

    public async Task<AccessMapView> GetAccess(string actorId, string key, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var tangent = await Community.Tangent.Get(key, ct)
            ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var actor = await Participant.Get(actorId, ct);
        var membership = await TangentMembership.Get(TangentMembership.Key(key, actorId), ct);
        var bag = TangentRoleAccess.InTangent(await roleAccess.Bag(actorId, ct), membership);
        var canManage = actor?.IsSuspended != true && await SpaceAdmission.Admitted(space, actorId, ct) && HoldsManage(space, tangent, actorId, membership, bag)
            && await Restrictions.ForTangent(actorId, key, clock.GetUtcNow(), ct) is null;
        if (!canManage) throw new TangentRuleViolation(TangentDenial.Forbidden, "Only a Tangent administrator can inspect its access settings.");
        var selected = (tangent.Access ?? AccessMap.TangentDefaults()).NormalizeForTangent();
        return new AccessMapView(selected, selected.ResolveTangent(), []);
    }

    public async Task<AccessMapView> SetAccess(string actorId, string key, AccessMap access, CancellationToken ct,
        bool publishExistingHistory = false)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var tangent = await Community.Tangent.Get(key, ct)
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorId, ct);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var membership = await TangentMembership.Get(TangentMembership.Key(key, actorId), ct);
            var bag = TangentRoleAccess.InTangent(await roleAccess.Bag(actorId, ct), membership);
            var canManage = actor?.IsSuspended != true
                && await SpaceAdmission.Admitted(space, actorId, ct) && HoldsManage(space, tangent, actorId, membership, bag)
                && await Restrictions.ForTangent(actorId, key, clock.GetUtcNow(), ct) is null;
            if (!canManage) throw new TangentRuleViolation(TangentDenial.Forbidden, "Only a Tangent administrator can change access.");
            var proposed = Community.Tangent.NormalizeAccess(access);
            if (!publishExistingHistory)
            {
                var query = new QueryDefinition { Sort = SortSpecParser.ParseStrict<Topic>(nameof(Topic.Id)) };
                for (var page = 1; ; page++)
                {
                    var candidates = await Topic.Query(topic => topic.TangentKey == key
                        && topic.ReadAudience == TopicReadAudience.Public, query.WithPagination(page, 100), ct);
                    foreach (var topic in candidates)
                        PublicHistory.RequireConsent(topic.IsPubliclyReadable(tangent),
                            PublicHistory.IsPublic(topic.ReadAudience, topic.Access ?? AccessMap.TopicDefaults(), proposed), false);
                    if (candidates.Count < 100) break;
                }
            }
            tangent.ChangeAccess(proposed, clock.GetUtcNow());
            await tangent.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.TangentChanged, "", actorId, tangentKey: key, ct: ct);
            await EntityContext.Commit(ct);
            signals.Pulse();
            var selected = tangent.Access!.NormalizeForTangent();
            return new AccessMapView(selected, selected.ResolveTangent(), []);
        }
        finally { gate.Exit(); }
    }

    public async Task<TangentTopicCreation> CreateTopic(string actorId, string tangentKey, string topicKey, string title,
        string? description, CancellationToken ct, TopicReadAudience? readAudience = null)
    {
        if (readAudience is not null && !Enum.IsDefined(readAudience.Value)) throw new ArgumentException("Choose Public or Admitted.");
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
            var bag = TangentRoleAccess.InTangent(await roleAccess.Bag(actorId, ct), actorMembership);
            var canCreate = TangentRoleAccess.CanDo(
                (tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent().CreateTopics, bag);
            if (actor?.IsSuspended == true || !await SpaceAdmission.Admitted(space, actorId, ct) || !IsAdmitted(space, tangent, actorId, actorMembership)
                || !tangent.ParticipationRights(actor?.Classification ?? ParticipantClassification.Undeclared).Write
                || !(space?.IsOwner(actorId) == true || tangent.IsOwner(actorId) || canCreate))
                throw new TangentRuleViolation(TangentDenial.Forbidden, "The current role cannot create Topics in this Tangent.");
            var now = clock.GetUtcNow();
            // Scoped restrictions apply to administration as well: a banned or timed-out delegated administrator
            // cannot create topics. The owner is never a restriction target.
            if (!tangent.IsOwner(actorId) && await Restrictions.ForTangent(actorId, tangent.Id, now, ct) is { BlockStarting: true })
                throw new TangentRuleViolation(TangentDenial.Forbidden, "A scoped restriction currently denies topic administration.");
            if (await Topic.Get(topicKey, ct) is not null) throw new TangentRuleViolation(TangentDenial.AlreadyExists, "That stable topic key already exists.");
            var topic = tangent.IsOwner(actorId)
                ? Topic.Create(space, actorId, topicKey, title, now, tangent)
                : Topic.CreateDelegated(space, actorId, topicKey, title, now, tangent);
            if (readAudience is { } audience)
            {
                topic.ReadAudience = audience;
                topic.MembersOnly = audience == TopicReadAudience.Restricted;
            }
            if (description is not null) topic.ChangeDescription(description, now);
            await topic.Save(ct);
            var audit = new TopicAudit { ActorParticipantId = actorId, TopicKey = topicKey, Operation = TopicAdministration.Create, Accepted = true,
                Reason = "Accepted.", SelectedPolicyRevision = topic.PolicyRevision, SpacePolicyRevision = space?.PolicyRevision ?? 0, OccurredAt = now };
            await audit.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.TopicChanged, topic.Id, actorId, tangentKey: tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            signals.Pulse();
            var policy = topic.CurrentPolicy(space, actorId, null, actor?.IsSuspended == true, tangent, actorMembership,
                actor?.Classification ?? ParticipantClassification.Undeclared);
            result = new TangentTopicCreation(new TopicAdministrationResult(true, null, "Accepted.", topic.Id, topic.PolicyRevision, audit.Id), TopicDescription.From(topic, policy, tangent));
        }
        finally { gate.Exit(); }
        return result;
    }

    /// <summary>Topic creation under delegated authority.
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
            signals.Pulse();
            result = new TangentMembershipResult(member.TangentKey, member.ParticipantId, member.Role);
        }
        finally { gate.Exit(); }
        return result;
    }

    /// <summary>Current card access for global activity entries that have no topic key.</summary>
    public async Task<bool> CanAccess(string did, string tangentKey, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var participant = await Participant.Get(did, ct);
        var tangent = await Community.Tangent.Get(tangentKey, ct);
        var membership = tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangentKey, did), ct);
        var bag = TangentRoleAccess.InTangent(await roleAccess.Bag(did, ct), membership);
        var allowed = space is not null && participant?.IsSuspended != true && tangent is not null
            && IsAdmitted(space, tangent, did, membership)
            && await Restrictions.ForTangent(did, tangentKey, clock.GetUtcNow(), ct) is null
            && (space.IsOwner(did) || tangent.ParticipationRights(participant?.Classification ?? ParticipantClassification.Undeclared).Read)
            && (space.IsOwner(did) || tangent.IsOwner(did) || TangentRoleAccess.CanDo(
                (tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent().See, bag));
        return allowed;
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
                manage ? ["read", "createTopic", "manageTangent", "manageParticipants"] : ["read"], new Dictionary<string,string>()),
            CanManageAdmission: tangent.IsOwner(actorId), Kind: tangent.Kind, ReadAudience: AudienceOf(tangent));
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
                var membership = did is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, did), ct);
                if (did is not null && !tangent.IsOwner(did) && space.IsOwner(did) != true
                    && await Restrictions.ForTangent(did, tangent.Id, clock.GetUtcNow(), ct) is { Banned: true }) continue;
                var unrestrictedAdmin = membership?.Role == TangentRole.Admin
                    && await Restrictions.ForTangent(did!, tangent.Id, clock.GetUtcNow(), ct) is null;
                if (!tangent.IsOwner(did) && space?.IsOwner(did) != true && !unrestrictedAdmin
                    && !TangentRoleAccess.CanDo((tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent().See, TangentRoleAccess.InTangent(bag, membership))) continue;
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
        Tangent? selectedTangent, TangentMembership? selectedMembership, int page, bool includeManage, CancellationToken ct,
        int pageSize = TopicsPerTangent)
    {
        if (space is null || participant?.IsSuspended == true) return new TopicSlice([], null);
        var skipped = checked((page - 1) * pageSize);
        var output = new List<TopicDescription>(pageSize + 1);
        var visible = 0;
        var scanned = 0;
        var tangentCache = new Dictionary<string, Tangent?>();
        var membershipCache = new Dictionary<string, TangentMembership?>();
        var now = clock.GetUtcNow();
        for (var storagePage = 1; ; storagePage++)
        {
            var topicOrder = selectedTangent?.Kind == TangentKind.Articles ? articles : topics;
            var candidates = tangentKey is null
                ? await Topic.Query(topic => true, topics.WithPagination(storagePage, TopicsPerTangent), ct)
                : await Topic.Query(topic => topic.TangentKey == tangentKey, topicOrder.WithPagination(storagePage, TopicsPerTangent), ct);
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
                var topicMembership = did is null ? null : await TopicMembership.Get(TopicMembership.Key(topic.Id, did), ct);
                // A scoped denial scrubs the topic from this directory; the topic record is checked
                // before its Tangent's inherited record.
                var restriction = did is null ? null
                    : await Restrictions.ForTopic(did, topic.Id, topic.TangentKey, now, ct);
                var policy = await roleAccess.ProjectTopic(topic, tangent,
                    topic.CurrentPolicy(space, did, topicMembership, participant?.IsSuspended == true, tangent, membership,
                        participant?.Classification ?? ParticipantClassification.Undeclared, restriction),
                    participant?.IsSuspended == true, restriction,
                    participant?.Classification ?? ParticipantClassification.Undeclared, ct);
                if (did is null && topic.IsPubliclyReadable(tangent))
                    policy = policy with { CanRead = true, Reason = "allowed" };
                if (!policy.CanRead && !(includeManage && policy.CanManage)) continue;
                if (visible++ < skipped) continue;
                output.Add(TopicDescription.From(topic, policy, tangent));
                if (output.Count > pageSize)
                    return new TopicSlice(output.Take(pageSize).ToArray(), page + 1, false);
            }
            if (candidates.Count < TopicsPerTangent) return new TopicSlice(output, null, false);
        }
    }

    private static void CheckPage(int page)
    {
        if (page is < 1 or > MaximumPage)
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "Choose a directory page between 1 and 10000.");
    }

    private static bool IsAdmitted(Space? space, Community.Tangent tangent, string? actorId, TangentMembership? membership)
        => space?.IsOwner(actorId) == true || tangent.CanParticipate(actorId, membership);

    private static string AudienceOf(Community.Tangent tangent)
    {
        var see = (tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent().See!;
        return see.Contains(AccessTokens.Everyone) ? "Public"
            : see.Contains(AccessTokens.Authenticated) || see.Contains("role:participant") ? "Signed in" : "Admitted";
    }

    private static bool HoldsManage(Space? space, Community.Tangent tangent, string? actorId,
        TangentMembership? membership, IReadOnlyCollection<string> bag)
        => IsAdmitted(space, tangent, actorId, membership)
            && (space?.IsOwner(actorId) == true || tangent.IsOwner(actorId) || membership?.Role == TangentRole.Admin
                || TangentRoleAccess.CanDo((tangent.Access ?? AccessMap.TangentDefaults()).ResolveTangent().Manage, bag));

    private sealed record TangentSlice(IReadOnlyList<Tangent> Items, int? NextPage, bool ScanLimited = false);
    private sealed record TopicSlice(IReadOnlyList<TopicDescription> Items, int? NextPage, bool ScanLimited = false);
}

public sealed record SummaryTopicSlice(IReadOnlyList<TopicDescription> Topics, bool Incomplete);
