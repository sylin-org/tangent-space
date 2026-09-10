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
public sealed class TangentGovernance(TimeProvider clock, PolicyGate gate)
{
    public const int TangentsPerPage = 50;
    public const int ChannelsPerTangent = 100;
    public const int MaximumPage = 10000;
    // The prototype deliberately caps policy work. A scan-limit flag says the caller
    // did not receive a complete directory; it never reports a private count.
    public const int MaximumDirectoryScan = 500;
    private static readonly QueryDefinition tangents = new() { Sort = SortSpecParser.ParseStrict<TangentCommunity>(nameof(TangentCommunity.Id)), CountStrategy = CountStrategy.Exact };
    private static readonly QueryDefinition rooms = new() { Sort = SortSpecParser.ParseStrict<Room>(nameof(Room.Id)), CountStrategy = CountStrategy.Exact };

    public Task<TangentsResponse> List(string? actorDid, CancellationToken ct) => List(actorDid, 1, 1, ct);

    public Task<TangentsResponse> List(string? actorDid, int page, CancellationToken ct) => List(actorDid, page, 1, ct);

    public async Task<TangentsResponse> List(string? actorDid, int page, int channelPage, CancellationToken ct)
    {
        CheckPage(page); CheckPage(channelPage);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            var home = await TangentBootstrap.EnsureHome(site, clock, ct);
            var participant = actorDid is null ? null : await Participant.Get(actorDid, ct);
            if (participant?.IsSuspended == true)
            {
                await EntityContext.Commit(ct);
                return new TangentsResponse([], false, false, page);
            }
            var selected = await VisibleTangents(site, actorDid, page, ct);
            var visible = new List<TangentDescription>(selected.Items.Count);
            foreach (var tangent in selected.Items)
            {
                var membership = actorDid is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorDid), ct);
                var owner = tangent.IsOwner(actorDid);
                var channels = await VisibleChannels(site, actorDid, participant, tangent.Id, tangent, membership, channelPage, includeManage: true, ct);
                visible.Add(new TangentDescription(tangent.Id, tangent.Name, tangent.Description, tangent.Motto, tangent.Accent, tangent.Artwork,
                    tangent.OwnerDid, owner, owner, channels.Items, channels.NextPage is not null, channels.NextPage, channels.ScanLimited));
            }
            await EntityContext.Commit(ct);
            return new TangentsResponse(visible, site?.IsOwner(actorDid) == true,
                site is null || site.IsOwner(actorDid) && home?.SetupComplete != true, page, selected.NextPage, selected.ScanLimited);
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

    public async Task<TangentDescription> Create(string actorDid, string key, string name, string? description, string? motto,
        string? accent, string? artwork, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            var actor = await Participant.Get(actorDid, ct);
            if (actor?.IsSuspended == true) throw new TangentRuleViolation(TangentDenial.Forbidden, "A suspended participant cannot create a Tangent.");
            var existing = await TangentCommunity.Get(key, ct);
            if (existing is not null)
            {
                if (key != TangentCommunity.HomeKey || existing.SetupComplete || !existing.IsOwner(actorDid))
                    throw new TangentRuleViolation(TangentDenial.AlreadyExists, "That Tangent key already exists.");
                existing.Change(actorDid, name, description, motto, accent, artwork, clock.GetUtcNow());
                await existing.Save(ct);
                await ActivityJournal.AppendInTransaction(ActivityKind.TangentChanged, "", actorDid, tangentKey: existing.Id, ct: ct);
                await EntityContext.Commit(ct);
                ActivityJournal.SignalAfterCommit();
                return new TangentDescription(existing.Id, existing.Name, existing.Description, existing.Motto, existing.Accent, existing.Artwork,
                    existing.OwnerDid, true, true, []);
            }
            var tangent = TangentCommunity.Create(site, actorDid, key, name, description, motto, accent, artwork, clock.GetUtcNow());
            await tangent.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.TangentChanged, "", actorDid, tangentKey: tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return new TangentDescription(tangent.Id, tangent.Name, tangent.Description, tangent.Motto, tangent.Accent, tangent.Artwork,
                tangent.OwnerDid, true, true, []);
        }
        finally { gate.Exit(); }
    }

    public async Task<TangentDescription> Change(string actorDid, string key, string? name, string? description, string? motto,
        string? accent, string? artwork, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            var tangent = await TangentCommunity.Get(key, ct) ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorDid, ct);
            if (actor?.IsSuspended == true) throw new TangentRuleViolation(TangentDenial.Forbidden, "A suspended participant cannot manage a Tangent.");
            tangent.Change(actorDid, name, description, motto, accent, artwork, clock.GetUtcNow());
            await tangent.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.TangentChanged, "", actorDid, tangentKey: tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return await DescribeForOwner(tangent, actorDid, ct);
        }
        finally { gate.Exit(); }
    }

    public async Task<TangentChannelCreation> CreateChannel(string actorDid, string tangentKey, string roomKey, string title,
        RoomAdmission admission, string? topic, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            var tangent = await TangentCommunity.Get(tangentKey, ct) ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorDid, ct);
            if (actor?.IsSuspended == true || !tangent.IsOwner(actorDid)) throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the current Tangent owner can create its channels.");
            if (await Room.Get(roomKey, ct) is not null) throw new TangentRuleViolation(TangentDenial.AlreadyExists, "That stable channel key already exists.");
            var now = clock.GetUtcNow();
            var room = Room.Create(site, actorDid, roomKey, title, admission, now, tangentKey, tangent);
            if (topic is not null) room.ChangeTopic(site, actorDid, null, topic, now, tangent);
            await room.Save(ct);
            var audit = new RoomAudit { ActorDid = actorDid, RoomKey = roomKey, Operation = RoomAdministration.Create, Accepted = true,
                Reason = "Accepted.", SelectedPolicyRevision = room.PolicyRevision, SitePolicyRevision = site?.PolicyRevision ?? 0, OccurredAt = now };
            await audit.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.RoomChanged, room.Id, actorDid, tangentKey: tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            var policy = room.CurrentPolicy(site, actorDid, null, false, tangent, null);
            return new TangentChannelCreation(new RoomAdministrationResult(true, null, "Accepted.", room.Id, room.PolicyRevision, room.SpaceUri, audit.Id), RoomDescription.From(room, policy));
        }
        finally { gate.Exit(); }
    }

    /// <summary>Invitation workflows can call this now; the human invitation endpoint remains a later scope.</summary>
    public async Task<TangentMembershipResult> SetMembership(string actorDid, string tangentKey, string targetDid, TangentRole role, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var tangent = await TangentCommunity.Get(tangentKey, ct) ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var actor = await Participant.Get(actorDid, ct);
            if (actor?.IsSuspended == true || !tangent.IsOwner(actorDid)) throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the current Tangent owner can change community membership.");
            if (!IdentityResolver.IsValidDid(targetDid) || tangent.IsOwner(targetDid)) throw new TangentRuleViolation(TangentDenial.InvalidInput, "Choose a non-owner AT DID for membership.");
            if (!Enum.IsDefined(role)) throw new TangentRuleViolation(TangentDenial.InvalidInput, "Choose member or removed.");
            var member = TangentMembership.Assign(tangent, targetDid, role, actorDid, clock.GetUtcNow());
            await member.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.MembershipChanged, "", actorDid, targetDid, tangent.Id, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return new TangentMembershipResult(member.TangentKey, member.ParticipantDid, member.Role);
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
            var allowed = site is not null && participant?.IsSuspended != true && tangent?.CanParticipate(did, membership) == true;
            await EntityContext.Commit(ct);
            return allowed;
        }
        finally { gate.Exit(); }
    }

    private async Task<TangentDescription> DescribeForOwner(TangentCommunity tangent, string actorDid, CancellationToken ct)
    {
        var site = await TangentSite.Get(TangentConstants.SiteId, ct);
        var participant = await Participant.Get(actorDid, ct);
        var channels = await VisibleChannels(site, actorDid, participant, tangent.Id, tangent, null, 1, includeManage: true, ct);
        return new TangentDescription(tangent.Id, tangent.Name, tangent.Description, tangent.Motto, tangent.Accent, tangent.Artwork,
            tangent.OwnerDid, true, true, channels.Items, channels.NextPage is not null, channels.NextPage, channels.ScanLimited);
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
                if (!tangent.CanParticipate(did, membership)) continue;
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
                var policy = room.CurrentPolicy(site, did, roomMembership, false, tangent, membership);
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
