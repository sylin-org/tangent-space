using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Site;
using TangentSpace.Communities;
using TangentSpace.Activity;
using TangentSpace.Authorization;

namespace TangentSpace.Rooms;

/// <summary>Register once as a singleton; policy and acceptance share the host's arrival gate.</summary>
public sealed class RoomGovernance(TimeProvider clock, PolicyGate gate, TangentSpace.Participants.ParticipantDirectory directory,
    TangentRoles? roles = null)
{
    private static readonly QueryDefinition directoryQuery = new QueryDefinition
    {
        Sort = SortSpecParser.ParseStrict<Room>(nameof(Room.Id)),
        CountStrategy = CountStrategy.Exact
    };

    public async Task<RoomListing> List(string? actorId, int page, CancellationToken ct)
    {
        if (page is < 1 or > RoomConstants.MaximumPage)
            throw new RoomRuleViolation(RoomDenial.InvalidInput, "Choose a room page between 1 and 10000.");
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            var participant = actorId is null ? null : await Participant.Get(actorId, ct);
            var result = await Room.AllWithCount(directoryQuery.WithPagination(page, RoomConstants.PageSize), ct);
            var now = clock.GetUtcNow();
            var descriptions = new List<RoomDescription>(result.Items.Count);
            foreach (var room in result.Items)
            {
                var membership = actorId is null ? null : await RoomMembership.Get(RoomMembership.Key(room.Id, actorId), ct);
                var tangent = await TangentCommunity.Get(room.TangentKey, ct);
                var tangentMembership = actorId is null || tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
                var restriction = actorId is null ? null : await Restrictions.ForRoom(actorId, room.Id, room.TangentKey, now, ct);
                var policy = await Project(room.CurrentPolicy(site, actorId, membership, participant?.IsSuspended == true, tangent, tangentMembership,
                    participant?.Classification ?? ParticipantClassification.Undeclared, restriction), ct);
                // A directory must not become an oracle for invitation-only channel names.
                if (policy.CanRead || policy.CanManage) descriptions.Add(RoomDescription.From(room, policy));
            }
            await EntityContext.Commit(ct);
            return new RoomListing(descriptions, page, result.HasNextPage ? page + 1 : null, clock.GetUtcNow());
        }
        finally { gate.Exit(); }
    }

    public Task<RoomDescription?> Describe(string? actorId, string roomKey, CancellationToken ct)
        => WithCurrentPolicy<RoomDescription?>(actorId, roomKey, async (policy, token) =>
        {
            var room = await Room.Get(roomKey, token);
            return room is null || (!policy.CanRead && !policy.CanManage) ? null : RoomDescription.From(room, policy);
        }, ct);

    /// <summary>Bounded actor-filtered topic directory for one Tangent.</summary>
    public async Task<TangentChannelDirectory> ListForTangent(string? actorId, string tangentKey, int page, CancellationToken ct)
    {
        if (page is < 1 or > RoomConstants.MaximumPage)
            throw new RoomRuleViolation(RoomDenial.InvalidInput, "Choose a room page between 1 and 10000.");
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            var participant = actorId is null ? null : await Participant.Get(actorId, ct);
            var tangent = await TangentCommunity.Get(tangentKey, ct);
            if (tangent is null || participant?.IsSuspended == true) return new TangentChannelDirectory([], page, null);
            var tangentMembership = actorId is null ? null : await TangentMembership.Get(TangentMembership.Key(tangentKey, actorId), ct);
            var rooms = await Room.QueryWithCount(room => room.TangentKey == tangentKey,
                directoryQuery.WithPagination(page, RoomConstants.PageSize), ct);
            var now = clock.GetUtcNow();
            var descriptions = new List<RoomDescription>(rooms.Items.Count);
            foreach (var room in rooms.Items)
            {
                var membership = actorId is null ? null : await RoomMembership.Get(RoomMembership.Key(room.Id, actorId), ct);
                var restriction = actorId is null ? null : await Restrictions.ForRoom(actorId, room.Id, room.TangentKey, now, ct);
                var policy = await Project(room.CurrentPolicy(site, actorId, membership, false, tangent, tangentMembership,
                    participant?.Classification ?? ParticipantClassification.Undeclared, restriction), ct);
                if (policy.CanRead || policy.CanManage) descriptions.Add(RoomDescription.From(room, policy));
            }
            await EntityContext.Commit(ct);
            return new TangentChannelDirectory(descriptions, page, rooms.HasNextPage ? page + 1 : null);
        }
        finally { gate.Exit(); }
    }

    public async Task<RoomAdministrationResult> SetSuspension(string actorId, string targetIdentifier, bool suspended, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            var participant = (await directory.ByIdentifier(targetIdentifier, ct))?.Participant;
            var targetId = participant?.Id;
            var actor = await Participant.Get(actorId, ct);
            var denial = site?.IsOwner(actorId) != true || actor?.IsSuspended == true || targetId is null || site.IsOwner(targetId)
                ? new RoomRuleViolation(RoomDenial.Forbidden, "Only the current site owner can suspend or restore other participants.")
                : participant is null ? new RoomRuleViolation(RoomDenial.NotFound, "The participant must first establish a verified arrival.") : null;
            if (denial is null)
            {
                participant!.IsSuspended = suspended;
                site!.PolicyRevision = checked(site.PolicyRevision + 1);
                await participant.Save(ct);
                await site.Save(ct);
                await ActivityJournal.AppendInTransaction(ActivityKind.ParticipantChanged, "", actorId, targetId, ct: ct);
            }
            var audit = new RoomAudit
            {
                ActorParticipantId = actorId, TargetParticipantId = targetId, Operation = RoomAdministration.SetSuspension,
                Accepted = denial is null, Denial = denial?.Denial,
                Reason = denial?.Message ?? (suspended ? "Participant suspended." : "Participant restored."),
                SitePolicyRevision = site?.PolicyRevision ?? 0, OccurredAt = clock.GetUtcNow()
            };
            await audit.Save(ct);
            await EntityContext.Commit(ct);
            if (denial is null) ActivityJournal.SignalAfterCommit();
            return new RoomAdministrationResult(audit.Accepted, audit.Denial, audit.Reason, "",
                audit.SitePolicyRevision, null, audit.Id);
        }
        finally { gate.Exit(); }
    }

    public Task<RoomAdministrationResult> Create(string actorId, string roomKey, string title, RoomAdmission admission, CancellationToken ct)
        => Administer(actorId, roomKey, null, RoomAdministration.Create, null,
            (site, existing, _, _, _, _, _, now) => existing is not null
                ? throw new RoomRuleViolation(RoomDenial.AlreadyExists, "That stable room key already exists; reconcile its pending Space instead of creating another room.")
                : new Change(Room.Create(site, actorId, roomKey, title, admission, now)), ct);

    public Task<RoomAdministrationResult> SetMembership(string actorId, string roomKey, string targetIdentifier, RoomRole role, CancellationToken ct)
        => Administer(actorId, roomKey, targetIdentifier, RoomAdministration.SetMembership, role,
            (site, room, actor, targetId, target, tangent, tangentMembership, now) =>
            {
                var current = RequireRoom(room);
                return new Change(current, current.ChangeMembership(site, actorId, actor, targetId!, target, role, now, tangent, tangentMembership));
            }, ct);

    public Task<RoomAdministrationResult> SetTopic(string actorId, string roomKey, string topic, CancellationToken ct)
        => Administer(actorId, roomKey, null, RoomAdministration.SetTopic, null,
            (site, room, actor, _, _, tangent, tangentMembership, now) =>
            {
                var current = RequireRoom(room);
                current.ChangeTopic(site, actorId, actor, topic, now, tangent, tangentMembership);
                return new Change(current);
            }, ct);

    public Task<RoomAdministrationResult> SetAdmission(string actorId, string roomKey, RoomAdmission admission, CancellationToken ct)
        => Administer(actorId, roomKey, null, RoomAdministration.SetAdmission, null,
            (site, room, _, _, _, tangent, _, now) =>
            {
                var current = RequireRoom(room);
                current.ChangeAdmission(site, actorId, admission, now, tangent);
                return new Change(current);
            }, ct);

    public Task<RoomAdministrationResult> SetReadAudience(string actorId, string roomKey, RoomReadAudience audience,
        bool publishExistingHistory, CancellationToken ct)
        => Administer(actorId, roomKey, null, RoomAdministration.SetReadAudience, null,
            (site, room, _, _, _, tangent, _, now) =>
            {
                var current = RequireRoom(room);
                current.ChangeReadAudience(site, actorId, audience, publishExistingHistory, now, tangent);
                return new Change(current);
            }, ct, audience, publishExistingHistory);

    public Task<RoomAdministrationResult> SetSettings(string actorId, string roomKey, bool allowPostEditing, bool isLocked,
        string? title, string? topic, CancellationToken ct)
        => Administer(actorId, roomKey, null, RoomAdministration.SetSettings, null,
            (site, room, actor, _, _, tangent, tangentMembership, now) =>
            {
                var current = RequireRoom(room);
                current.ChangeSettings(site, actorId, actor, allowPostEditing, isLocked, title, topic, now, tangent, tangentMembership);
                return new Change(current);
            }, ct);

    /// <summary>Audited authorization before external provisioning. The network call happens after this gate is released.</summary>
    public Task<RoomAdministrationResult> BeginProvisioning(string actorId, string roomKey, CancellationToken ct)
        => Administer(actorId, roomKey, null, RoomAdministration.Provision, null,
            (site, room, actor, _, _, tangent, tangentMembership, _) =>
            {
                var current = RequireRoom(room);
                // A delegated channel administrator provisions without gaining ownership.
                if (!current.CurrentPolicy(site, actorId, actor, false, tangent, tangentMembership).CanManage)
                    throw new RoomRuleViolation(RoomDenial.Forbidden, "Only the owner or a current channel administrator can provision a room.");
                return new Change(current);
            }, ct);

    /// <summary>Called after the real transport verifies authority/type/key. Retries preserve an existing identical mapping.</summary>
    public Task<RoomAdministrationResult> MapSpace(string actorId, string roomKey, long expectedRevision, string spaceUri, CancellationToken ct)
        => Administer(actorId, roomKey, null, RoomAdministration.MapSpace, null,
            (site, room, _, _, _, tangent, tangentMembership, now) =>
            {
                var current = RequireRoom(room);
                current.CompleteSpace(site, actorId, expectedRevision, spaceUri, now, tangent, tangentMembership);
                return new Change(current);
            }, ct);

    /// <summary>
    /// Reload current authority and execute bounded local work under the same gate and transaction as administration.
    /// Do not call PDS/network services, nest a transaction, or reenter RoomGovernance/Arrival inside the callback.
    /// A denied snapshot still carries the selected revisions so source acceptance can retain its decision.
    /// </summary>
    public async Task<T> WithCurrentPolicy<T>(string? actorId, string roomKey,
        Func<RoomPolicy, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AcceptanceTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            var room = await Room.Get(roomKey, ct);
            var membership = room is null || actorId is null ? null
                : await RoomMembership.Get(RoomMembership.Key(roomKey, actorId), ct);
            var participant = actorId is null ? null : await Participant.Get(actorId, ct);
            var tangent = room is null ? null : await TangentCommunity.Get(room.TangentKey, ct);
            var tangentMembership = actorId is null || tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
            var restriction = room is null || actorId is null ? null
                : await Restrictions.ForRoom(actorId, room.Id, room.TangentKey, clock.GetUtcNow(), ct);
            var policy = room is null ? null : await Project(room.CurrentPolicy(site, actorId, membership, participant?.IsSuspended == true, tangent, tangentMembership,
                participant?.Classification ?? ParticipantClassification.Undeclared, restriction), ct);
            policy ??= new RoomPolicy(roomKey, actorId, 0, site?.PolicyRevision ?? 0, RoomAdmission.InvitationOnly,
                    RoomSpaceState.Pending, null, null, false, false, false, false, false, "room-not-found");
            var result = await operation(policy, ct);
            await EntityContext.Commit(ct);
            return result;
        }
        finally { gate.Exit(); }
    }

    private async Task<RoomAdministrationResult> Administer(string actorId, string roomKey, string? targetIdentifier,
        RoomAdministration operation, RoomRole? requestedRole,
        Func<TangentSite?, Room?, RoomMembership?, string?, RoomMembership?, TangentCommunity?, TangentMembership?, DateTimeOffset, Change> apply,
        CancellationToken ct, RoomReadAudience? requestedReadAudience = null, bool? publishExistingHistory = null)
    {
        RoomAdministrationResult result;
        var changed = false;
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var now = clock.GetUtcNow();
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            await TangentBootstrap.EnsureHome(site, clock, ct);
            // Administration targets arrive as external identifiers and resolve to participant ids here.
            var targetId = targetIdentifier is null ? null
                : (await directory.ByIdentifier(targetIdentifier, ct))?.Participant.Id
                ?? throw new RoomRuleViolation(RoomDenial.NotFound, "The participant must first establish a verified arrival.");
            Room? room = null;
            Change? change = null;
            RoomRuleViolation? denial = null;
            try
            {
                Room.CheckKey(roomKey);
                room = await Room.Get(roomKey, ct);
                if ((await Participant.Get(actorId, ct))?.IsSuspended == true)
                    throw new RoomRuleViolation(RoomDenial.Forbidden, "A suspended participant cannot administer rooms.");
                // Scoped restrictions apply to administration as well as conversation: a banned or timed-out
                // participant cannot administer here. Owners are never restriction targets.
                if (room is not null && await Restrictions.ForRoom(actorId, room.Id, room.TangentKey, now, ct) is not null)
                    throw new RoomRuleViolation(RoomDenial.Forbidden, "A scoped restriction currently denies this administration.");
                var actor = room is null ? null : await RoomMembership.Get(RoomMembership.Key(roomKey, actorId), ct);
                var target = room is null || targetId is null ? null
                    : await RoomMembership.Get(RoomMembership.Key(roomKey, targetId), ct);
                var tangent = room is null ? null : await TangentCommunity.Get(room.TangentKey, ct);
                var tangentMembership = tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangent.Id, actorId), ct);
                if (room is not null)
                {
                    var projected = await Project(room.CurrentPolicy(site, actorId, actor, false, tangent, tangentMembership), ct);
                    var allowed = operation switch
                    {
                        RoomAdministration.SetMembership => projected.CanManage,
                        RoomAdministration.SetAdmission or RoomAdministration.SetReadAudience => projected.CanAppointManagers,
                        _ => projected.CanManage
                    };
                    if (!allowed) throw new RoomRuleViolation(RoomDenial.Forbidden, "The current role does not permit this Topic operation.");
                }
                // The domain command, membership row and audit use the same once-resolved identity.
                change = apply(site, room, actor, targetId, target, tangent, tangentMembership, now);
            }
            catch (RoomRuleViolation rejected) { denial = rejected; }

            // Policy errors are ordinary audited denials. Persistence failures propagate and roll back everything.
            if (change is not null)
            {
                room = change.Room;
                await room.Save(ct);
                if (change.Membership is not null) await change.Membership.Save(ct);
                var kind = operation == RoomAdministration.SetMembership ? ActivityKind.MembershipChanged : ActivityKind.RoomChanged;
                await ActivityJournal.AppendInTransaction(kind, room.Id, actorId, change.Membership?.ParticipantId, room.TangentKey, ct: ct);
            }
            var audit = new RoomAudit
            {
                ActorParticipantId = actorId, RoomKey = roomKey, TargetParticipantId = targetId, Operation = operation,
                RequestedRole = requestedRole, RequestedReadAudience = requestedReadAudience,
                PublishExistingHistory = publishExistingHistory, Accepted = denial is null, Denial = denial?.Denial,
                Reason = denial?.Message ?? "Accepted.", SelectedPolicyRevision = room?.PolicyRevision ?? 0,
                SitePolicyRevision = site?.PolicyRevision ?? 0, OccurredAt = now
            };
            await audit.Save(ct);
            if (audit.Accepted)
                await CommandCommit.Report(new RoomAdministrationResult(true, null, audit.Reason, roomKey,
                    audit.SelectedPolicyRevision, room?.SpaceUri, audit.Id), ct);
            await EntityContext.Commit(ct);
            if (change is not null) ActivityJournal.SignalAfterCommit();
            changed = change is not null;
            result = new RoomAdministrationResult(audit.Accepted, audit.Denial, audit.Reason, roomKey,
                audit.SelectedPolicyRevision, room?.SpaceUri, audit.Id);
        }
        finally { gate.Exit(); }
        if (changed && roles is not null) await roles.ReconcileTopic(roomKey, ct);
        return result;
    }

    private static Room RequireRoom(Room? room)
        => room ?? throw new RoomRuleViolation(RoomDenial.NotFound, "The requested room does not exist.");

    private Task<RoomPolicy> Project(RoomPolicy policy, CancellationToken ct)
        => roles?.ProjectTopic(policy, ct) ?? Task.FromResult(policy);

    private sealed record Change(Room Room, RoomMembership? Membership = null);
}
