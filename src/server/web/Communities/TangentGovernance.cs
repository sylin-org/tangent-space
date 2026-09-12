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

/// <summary>Coordinates card ownership, community membership and channel creation under the host policy gate.</summary>
public sealed class TangentGovernance(TimeProvider clock, PolicyGate gate, Microsoft.Extensions.Options.IOptions<TangentSpace.Conversation.ConversationOptions> conversation,
    TangentSpace.Participants.ParticipantDirectory directory)
{
    public const int TangentsPerPage = 50;
    public const int ChannelsPerTangent = 100;
    public const int MaximumPage = 10000;
    // The prototype deliberately caps policy work. A scan-limit flag says the caller
    // did not receive a complete directory; it never reports a private count.
    public const int MaximumDirectoryScan = 500;
    private static readonly QueryDefinition tangents = new() { Sort = SortSpecParser.ParseStrict<TangentCommunity>(nameof(TangentCommunity.Id)), CountStrategy = CountStrategy.Exact };
    private static readonly QueryDefinition rooms = new() { Sort = SortSpecParser.ParseStrict<Room>(nameof(Room.Id)), CountStrategy = CountStrategy.Exact };

    public Task<TangentsResponse> List(string? actorId, CancellationToken ct) => List(actorId, 1, 1, ct);

    public Task<TangentsResponse> List(string? actorId, int page, CancellationToken ct) => List(actorId, page, 1, ct);

    /// <summary>Returns one actor-filtered Tangent card without using directory paging as an authorization oracle.</summary>
    public async Task<TangentDescription?> Describe(string? actorId, string key, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            var tangent = await TangentCommunity.Get(key, ct);
            if (tangent is null) return null;
            var participant = actorId is null ? null : await Participant.Get(actorId, ct);
            var membership = actorId is null ? null : await TangentMembership.Get(TangentMembership.Key(key, actorId), ct);
            if (site?.IsOwner(actorId) != true && !tangent.CanParticipate(actorId, membership)
                && (actorId is null || tangent.EffectiveAdmission != TangentAdmission.Approval)) return null;
            if (participant?.IsSuspended == true) return null;
            var owner = tangent.IsOwner(actorId) || site?.IsOwner(actorId) == true;
            var restricted = actorId is not null && !owner && await Restrictions.ForTangent(actorId, tangent.Id, clock.GetUtcNow(), ct) is not null;
            var canManage = owner || membership?.Role == TangentRole.Admin && !restricted;
            var canCreateTopic = canManage || tangent.AllowMemberTopics && membership?.Role is TangentRole.Member
                && !restricted && tangent.ParticipationRights(participant?.Classification ?? ParticipantClassification.Undeclared).Write;
            var channels = await VisibleChannels(site, actorId, participant, tangent.Id, tangent, membership, 1, includeManage: true, ct);
            var pending = actorId is not null && membership is null
                && await TangentJoinRequest.Get(TangentJoinRequest.Key(tangent.Id, actorId), ct) is { Pending: true };
            var result = new TangentDescription(tangent.Id, tangent.Name, tangent.Description, tangent.Motto, tangent.Accent, tangent.Artwork,
                tangent.OwnerParticipantId, owner, canManage, channels.Items, channels.NextPage is not null, channels.NextPage, channels.ScanLimited,
                owner || membership?.Role is TangentRole.Member or TangentRole.Admin or TangentRole.Reader, membership?.Role,
                tangent.EffectiveAdmission, pending,
                tangent.AllowMemberTopics, canCreateTopic,
                new TangentSpace.Authorization.PermissionView(owner ? "owner" : membership?.Role.ToString().ToLowerInvariant() ?? "visitor", "tangent",
                    canManage ? ["read", "createTopic", "manageTangent", "manageParticipants"] : canCreateTopic ? ["read", "createTopic"] : ["read"], new Dictionary<string, string>()));
            await EntityContext.Commit(ct);
            return result;
        }
        finally { gate.Exit(); }
    }

    public async Task<TangentsResponse> List(string? actorId, int page, int channelPage, CancellationToken ct)
    {
        CheckPage(page); CheckPage(channelPage);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            var home = await TangentBootstrap.EnsureHome(site, clock, ct);
            var participant = actorId is null ? null : await Participant.Get(actorId, ct);
            if (participant?.IsSuspended == true)
            {
                await EntityContext.Commit(ct);
                return new TangentsResponse([], false, false, page);
            }
            var selected = await VisibleTangents(site, actorId, page, ct);
            var visible = new List<TangentDescription>(selected.Items.Count);
            foreach (var tangent in selected.Items)
            {
                var membership = actorId is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
                var owner = tangent.IsOwner(actorId) || site?.IsOwner(actorId) == true;
                var restricted = actorId is not null && !owner && await Restrictions.ForTangent(actorId, tangent.Id, clock.GetUtcNow(), ct) is not null;
                var canManage = owner || membership?.Role == TangentRole.Admin && !restricted;
                var canCreateTopic = canManage || tangent.AllowMemberTopics && membership?.Role is TangentRole.Member && !restricted && tangent.ParticipationRights(participant?.Classification ?? ParticipantClassification.Undeclared).Write;
                var isMember = owner || membership?.Role is TangentRole.Member or TangentRole.Admin or TangentRole.Reader;
                var pending = !isMember && actorId is not null
                    && await TangentJoinRequest.Get(TangentJoinRequest.Key(tangent.Id, actorId), ct) is { Pending: true };
                var channels = await VisibleChannels(site, actorId, participant, tangent.Id, tangent, membership, channelPage, includeManage: true, ct);
                visible.Add(new TangentDescription(tangent.Id, tangent.Name, tangent.Description, tangent.Motto, tangent.Accent, tangent.Artwork,
                    tangent.OwnerParticipantId, owner, canManage, channels.Items, channels.NextPage is not null, channels.NextPage, channels.ScanLimited,
                    isMember, membership?.Role, tangent.EffectiveAdmission, pending, tangent.AllowMemberTopics, canCreateTopic,
                    new TangentSpace.Authorization.PermissionView(owner ? "owner" : membership?.Role.ToString().ToLowerInvariant() ?? "visitor", "tangent",
                        canManage ? new[] { "read", "createTopic", "manageTangent", "manageParticipants" } : canCreateTopic ? new[] { "read", "createTopic" } : new[] { "read" }, new Dictionary<string, string>())));
            }
            await EntityContext.Commit(ct);
            return new TangentsResponse(visible, site?.CanCreateTangent(participant) == true,
                site is null || site.IsOwner(actorId) && home?.SetupComplete != true, page, selected.NextPage, selected.ScanLimited);
        }
        finally { gate.Exit(); }
    }

    /// <summary>Bound, authorized directory for activity and channel continuation; page metadata never counts hidden rooms.</summary>
    public async Task<TangentChannelDirectory> ListAuthorizedChannels(string did, int page, CancellationToken ct)
    {
        CheckPage(page);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            var participant = await Participant.Get(did, ct);
            var result = participant?.IsSuspended == true
                ? new ChannelSlice([], null)
                : await VisibleChannels(site, did, participant, null, null, null, page, includeManage: false, ct);
            await EntityContext.Commit(ct);
            return new TangentChannelDirectory(result.Items, page, result.NextPage, result.ScanLimited);
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
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            var actor = await Participant.Get(actorId, ct);
            if (actor?.IsSuspended == true) throw new TangentRuleViolation(TangentDenial.Forbidden, "A suspended participant cannot create a Tangent.");
            var canCreate = site?.CanCreateTangent(actor) == true;
            if (!canCreate) throw new TangentRuleViolation(TangentDenial.Forbidden, "The server creation policy does not permit this participant.");
            var existing = await TangentCommunity.Get(key, ct);
            if (existing is not null)
            {
                if (key != TangentCommunity.HomeKey || existing.SetupComplete || !existing.IsOwner(actorId))
                    throw new TangentRuleViolation(TangentDenial.AlreadyExists, "That Tangent key already exists.");
                existing.Change(actorId, name, description, motto, accent, artwork, clock.GetUtcNow());
                await existing.Save(ct);
                await ActivityJournal.AppendInTransaction(ActivityKind.TangentChanged, "", actorId, tangentKey: existing.Id, ct: ct);
                await EntityContext.Commit(ct);
                ActivityJournal.SignalAfterCommit();
                return new TangentDescription(existing.Id, existing.Name, existing.Description, existing.Motto, existing.Accent, existing.Artwork,
                    existing.OwnerParticipantId, true, true, [], IsMember: true, Admission: existing.EffectiveAdmission);
            }
            if (site is null) throw new TangentRuleViolation(TangentDenial.Forbidden, "The server has not been claimed.");
            var tangent = site.IsOwner(actorId) || actor?.Classification == ParticipantClassification.Human || site.AllowAgentTangentOwnership
                ? TangentCommunity.CreateForAuthorizedActor(site, actorId, site.OwnerParticipantId, key, name, description, motto, accent, artwork, clock.GetUtcNow())
                : TangentCommunity.CreateForAuthorizedActor(site, site.OwnerParticipantId, site.OwnerParticipantId, key, name, description, motto, accent, artwork, clock.GetUtcNow());
            if (admission is { } initialAdmission)
                tangent.ChangeParticipationPolicy(tangent.OwnerParticipantId, initialAdmission, ParticipationPreset.Everyone, UndeclaredAccess.Write, clock.GetUtcNow());
            await tangent.Save(ct);
            if (!tangent.IsOwner(actorId)) await TangentMembership.Assign(tangent, actorId, TangentRole.Admin, site.OwnerParticipantId, clock.GetUtcNow()).Save(ct);
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
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            var actor = await Participant.Get(actorId, ct);
            if (site?.IsOwner(actorId) != true || actor is null || actor.IsSuspended)
                throw new UnauthorizedAccessException();
            var home = await TangentBootstrap.EnsureHome(site, clock, ct) ?? throw new UnauthorizedAccessException();
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
        string? accent, string? artwork, CancellationToken ct, bool? allowMemberTopics = null)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            var tangent = await TangentCommunity.Get(key, ct) ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorId, ct);
            if (actor?.IsSuspended == true) throw new TangentRuleViolation(TangentDenial.Forbidden, "A suspended participant cannot manage a Tangent.");
            var tangentMembership = await TangentMembership.Get(TangentMembership.Key(key, actorId), ct);
            var canManage = tangent.IsOwner(actorId) || site?.IsOwner(actorId) == true || tangentMembership?.Role == TangentRole.Admin && await Restrictions.ForTangent(actorId, tangent.Id, clock.GetUtcNow(), ct) is null;
            tangent.ChangeAuthorized(actorId, canManage, name, description, motto, accent, artwork, clock.GetUtcNow());
            if (allowMemberTopics is not null) tangent.ChangeTopicCreation(actorId, allowMemberTopics.Value, clock.GetUtcNow(), canManage);
            await tangent.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.TangentChanged, "", actorId, tangentKey: tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return await DescribeForOwner(tangent, actorId, ct);
        }
        finally { gate.Exit(); }
    }

    public async Task<TangentChannelCreation> CreateChannel(string actorId, string tangentKey, string roomKey, string title,
        RoomAdmission admission, string? topic, CancellationToken ct, bool membersOnly = false)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            var tangent = await TangentCommunity.Get(tangentKey, ct) ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorId, ct);
            var actorMembership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
            // The owner creates channels directly; a community administrator creates them under delegated
            // authority and never gains ownership of the channel or the Tangent.
            var delegatedAdmin = actorMembership?.Role == TangentRole.Admin;
            var delegatedMember = tangent.AllowMemberTopics && actorMembership?.Role == TangentRole.Member && tangent.ParticipationRights(actor?.Classification ?? ParticipantClassification.Undeclared).Write;
            if (actor?.IsSuspended == true || !(site?.IsOwner(actorId) == true || tangent.IsOwner(actorId) || delegatedAdmin || delegatedMember))
                throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the Tangent owner or a community administrator can create its channels.");
            var now = clock.GetUtcNow();
            // Scoped restrictions apply to administration as well: a banned or timed-out delegated administrator
            // cannot create channels. The owner is never a restriction target.
            if (!tangent.IsOwner(actorId) && await Restrictions.ForTangent(actorId, tangent.Id, now, ct) is not null)
                throw new TangentRuleViolation(TangentDenial.Forbidden, "A scoped restriction currently denies channel administration.");
            if (await Room.Get(roomKey, ct) is not null) throw new TangentRuleViolation(TangentDenial.AlreadyExists, "That stable channel key already exists.");
            var room = tangent.IsOwner(actorId)
                ? Room.Create(site, actorId, roomKey, title, admission, now, tangentKey, tangent, conversation.Value.NewTopicState)
                : Room.CreateDelegated(site, actorId, roomKey, title, admission, now, tangentKey, tangent, conversation.Value.NewTopicState);
            room.MembersOnly = membersOnly;
            if (topic is not null) room.ChangeTopic(site, actorId, null, topic, now, tangent, actorMembership);
            await room.Save(ct);
            var audit = new RoomAudit { ActorParticipantId = actorId, RoomKey = roomKey, Operation = RoomAdministration.Create, Accepted = true,
                Reason = "Accepted.", SelectedPolicyRevision = room.PolicyRevision, SitePolicyRevision = site?.PolicyRevision ?? 0, OccurredAt = now };
            await audit.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.RoomChanged, room.Id, actorId, tangentKey: tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            var policy = room.CurrentPolicy(site, actorId, null, actor?.IsSuspended == true, tangent, actorMembership,
                actor?.Classification ?? ParticipantClassification.Undeclared);
            return new TangentChannelCreation(new RoomAdministrationResult(true, null, "Accepted.", room.Id, room.PolicyRevision, room.SpaceUri, audit.Id), RoomDescription.From(room, policy));
        }
        finally { gate.Exit(); }
    }

    /// <summary>Invitation workflows can call this now; the human invitation endpoint remains a later scope.
    /// The target arrives as an external identifier (DID, internal DID, or handle) and resolves to
    /// its current holder here.</summary>
    public async Task<TangentMembershipResult> SetMembership(string actorId, string tangentKey, string targetIdentifier, TangentRole role, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var tangent = await TangentCommunity.Get(tangentKey, ct) ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorId, ct);
            var target = (await directory.ByIdentifier(targetIdentifier, ct))?.Participant
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The participant must first establish a verified arrival.");
            if (actor?.IsSuspended == true || !tangent.IsOwner(actorId)) throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the current Tangent owner can change community membership.");
            if (tangent.IsOwner(target.Id) || target.Id == actorId) throw new TangentRuleViolation(TangentDenial.InvalidInput, "Choose a non-owner participant for membership.");
            if (!Enum.IsDefined(role)) throw new TangentRuleViolation(TangentDenial.InvalidInput, "Choose member or removed.");
            var member = TangentMembership.Assign(tangent, target.Id, role, actorId, clock.GetUtcNow());
            await member.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.MembershipChanged, "", actorId, target.Id, tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return new TangentMembershipResult(member.TangentKey, member.ParticipantId, member.Role);
        }
        finally { gate.Exit(); }
    }

    /// <summary>Current card access for global activity entries that have no room key.</summary>
    public async Task<bool> CanAccess(string did, string tangentKey, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            var participant = await Participant.Get(did, ct);
            var tangent = await TangentCommunity.Get(tangentKey, ct);
            var membership = tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangentKey, did), ct);
            // Moderation denies activity even without room context; classification presets gate the same
            // participation the channels enforce, and ownership remains authority rather than participation.
            var allowed = site is not null && participant?.IsSuspended != true && tangent?.CanParticipate(did, membership) == true
                && await Restrictions.ForTangent(did, tangentKey, clock.GetUtcNow(), ct) is null
                && (tangent!.IsOwner(did) || tangent.ParticipationRights(participant?.Classification ?? ParticipantClassification.Undeclared).Read);
            await EntityContext.Commit(ct);
            return allowed;
        }
        finally { gate.Exit(); }
    }

    private async Task<TangentDescription> DescribeForOwner(TangentCommunity tangent, string actorId, CancellationToken ct)
    {
        var site = await TangentSite.Get(TangentConstants.SiteId, ct);
        var participant = await Participant.Get(actorId, ct);
        var membership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
        var owner = tangent.IsOwner(actorId) || site?.IsOwner(actorId) == true;
        var manage = owner || membership?.Role == TangentRole.Admin;
        var channels = await VisibleChannels(site, actorId, participant, tangent.Id, tangent, membership, 1, includeManage: true, ct);
        return new TangentDescription(tangent.Id, tangent.Name, tangent.Description, tangent.Motto, tangent.Accent, tangent.Artwork,
            tangent.OwnerParticipantId, owner, manage, channels.Items, channels.NextPage is not null, channels.NextPage, channels.ScanLimited,
            IsMember: true, MembershipRole: membership?.Role, Admission: tangent.EffectiveAdmission,
            AllowMemberTopics: tangent.AllowMemberTopics, CanCreateTopic: manage,
            Permissions: new TangentSpace.Authorization.PermissionView(owner ? "owner" : "admin", "tangent",
                manage ? ["read", "createTopic", "manageTangent", "manageParticipants"] : ["read"], new Dictionary<string,string>()));
    }

    private async Task<TangentSlice> VisibleTangents(TangentSite? site, string? did, int page, CancellationToken ct)
    {
        if (site is null) return new TangentSlice([], null);
        var skipped = checked((page - 1) * TangentsPerPage);
        var output = new List<TangentCommunity>(TangentsPerPage + 1);
        var visible = 0;
        var scanned = 0;
        for (var storagePage = 1; ; storagePage++)
        {
            var candidates = await TangentCommunity.AllWithCount(tangents.WithPagination(storagePage, TangentsPerPage), ct);
            foreach (var tangent in candidates.Items)
            {
                if (++scanned > MaximumDirectoryScan) return new TangentSlice(output, null, true);
                var membership = did is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, did), ct);
                // Invite-only tangents stay invisible to non-members. Approval tangents are discoverable
                // cards for signed-in participants so a join request can be made; their channels remain gated.
                if (site?.IsOwner(did) != true && !tangent.CanParticipate(did, membership)
                    && (did is null || tangent.EffectiveAdmission != TangentAdmission.Approval)) continue;
                if (visible++ < skipped) continue;
                output.Add(tangent);
                if (output.Count > TangentsPerPage)
                    return new TangentSlice(output.Take(TangentsPerPage).ToArray(), page + 1, false);
            }
            if (!candidates.HasNextPage) return new TangentSlice(output, null, false);
        }
    }

    private async Task<ChannelSlice> VisibleChannels(TangentSite? site, string? did, Participant? participant, string? tangentKey,
        TangentCommunity? selectedTangent, TangentMembership? selectedMembership, int page, bool includeManage, CancellationToken ct)
    {
        if (site is null || participant?.IsSuspended == true) return new ChannelSlice([], null);
        var skipped = checked((page - 1) * ChannelsPerTangent);
        var output = new List<RoomDescription>(ChannelsPerTangent + 1);
        var visible = 0;
        var scanned = 0;
        var tangentCache = new Dictionary<string, TangentCommunity?>();
        var membershipCache = new Dictionary<string, TangentMembership?>();
        var now = clock.GetUtcNow();
        for (var storagePage = 1; ; storagePage++)
        {
            var candidates = tangentKey is null
                ? await Room.Query(room => true, rooms.WithPagination(storagePage, ChannelsPerTangent), ct)
                : await Room.Query(room => room.TangentKey == tangentKey, rooms.WithPagination(storagePage, ChannelsPerTangent), ct);
            foreach (var room in candidates)
            {
                if (++scanned > MaximumDirectoryScan) return new ChannelSlice(output, null, true);
                var tangent = selectedTangent;
                var membership = selectedMembership;
                if (tangent is null)
                {
                    if (!tangentCache.TryGetValue(room.TangentKey, out tangent))
                    {
                        tangent = await TangentCommunity.Get(room.TangentKey, ct);
                        tangentCache[room.TangentKey] = tangent;
                    }
                    if (did is not null && !membershipCache.TryGetValue(room.TangentKey, out membership))
                    {
                        membership = tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(room.TangentKey, did), ct);
                        membershipCache[room.TangentKey] = membership;
                    }
                }
                var roomMembership = did is null ? null : await RoomMembership.Get(RoomMembership.Key(room.Id, did), ct);
                // A scoped denial scrubs the channel from this directory; the channel record is checked
                // before its Tangent's inherited record.
                var restriction = did is null ? null
                    : await Restrictions.ForRoom(did, room.Id, room.TangentKey, now, ct);
                var policy = room.CurrentPolicy(site, did, roomMembership, participant?.IsSuspended == true, tangent, membership,
                    participant?.Classification ?? ParticipantClassification.Undeclared, restriction);
                if (!policy.CanRead && !(includeManage && policy.CanManage)) continue;
                if (visible++ < skipped) continue;
                output.Add(RoomDescription.From(room, policy));
                if (output.Count > ChannelsPerTangent)
                    return new ChannelSlice(output.Take(ChannelsPerTangent).ToArray(), page + 1, false);
            }
            if (candidates.Count < ChannelsPerTangent) return new ChannelSlice(output, null, false);
        }
    }

    private static void CheckPage(int page)
    {
        if (page is < 1 or > MaximumPage)
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "Choose a directory page between 1 and 10000.");
    }

    private sealed record TangentSlice(IReadOnlyList<TangentCommunity> Items, int? NextPage, bool ScanLimited = false);
    private sealed record ChannelSlice(IReadOnlyList<RoomDescription> Items, int? NextPage, bool ScanLimited = false);
}
