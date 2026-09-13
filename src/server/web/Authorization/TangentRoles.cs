using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Koan.Identity.Roles;
using TangentSpace.Communities;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Authorization;

/// <summary>Seeds built-in roles and projects Koan decisions into the existing application policy snapshot.</summary>
public sealed class TangentRoles(IServiceScopeFactory scopes, TangentRoleContext context)
{
    private const int BatchSize = 100;
    private readonly SemaphoreSlim reconcile = new(1, 1);

    public async Task Reconcile(CancellationToken ct = default)
    {
        await reconcile.WaitAsync(ct);
        try
        {
            var site = await TangentSite.Get("site", ct);
            if (site is null || string.IsNullOrWhiteSpace(site.OwnerParticipantId)) return;
            using var actor = context.Bind(site.OwnerParticipantId);
            using var serviceScope = scopes.CreateScope();
            var engine = serviceScope.ServiceProvider.GetRequiredService<RoleEngine>();
            var participant = await EnsureHost(engine, site, ct);
            await Each<Participant>(async value => await engine.Assign(new(TangentRoleScopes.HostScope, value.Id, participant.Id,
                ScopedRolePropagation.Descendants), ct), ct);

            await Each<TangentCommunity>(value => EnsureTangent(engine, value, syncMemberships: false, ct), ct);
            await Each<TangentMembership>(value => SyncTangentMembership(engine, value, ct), ct);

            await Each<Room>(value => EnsureTopic(engine, value, syncMemberships: false, ct), ct);
            await Each<RoomMembership>(value => SyncRoomMembership(engine, value, ct), ct);
        }
        finally { reconcile.Release(); }
    }

    /// <summary>Synchronizes one Tangent and its direct memberships after a committed domain change.</summary>
    public async Task ReconcileTangent(string tangentKey, CancellationToken ct = default)
    {
        await reconcile.WaitAsync(ct);
        try
        {
            var site = await TangentSite.Get("site", ct);
            var tangent = await TangentCommunity.Get(tangentKey, ct);
            if (site is null || tangent is null || string.IsNullOrWhiteSpace(site.OwnerParticipantId)) return;
            using var actor = context.Bind(site.OwnerParticipantId);
            using var serviceScope = scopes.CreateScope();
            var engine = serviceScope.ServiceProvider.GetRequiredService<RoleEngine>();
            await EnsureHost(engine, site, ct);
            await EnsureTangent(engine, tangent, syncMemberships: true, ct);
        }
        finally { reconcile.Release(); }
    }

    /// <summary>Gives a newly arrived persistent participant the Host baseline role.</summary>
    public async Task ReconcileParticipant(string participantId, CancellationToken ct = default)
    {
        await reconcile.WaitAsync(ct);
        try
        {
            var site = await TangentSite.Get("site", ct);
            if (site is null || string.IsNullOrWhiteSpace(site.OwnerParticipantId)
                || await Participant.Get(participantId, ct) is null) return;
            using var actor = context.Bind(site.OwnerParticipantId);
            using var serviceScope = scopes.CreateScope();
            var engine = serviceScope.ServiceProvider.GetRequiredService<RoleEngine>();
            var participant = await EnsureHost(engine, site, ct);
            await engine.Assign(new(TangentRoleScopes.HostScope, participantId, participant.Id,
                ScopedRolePropagation.Descendants), ct);
        }
        finally { reconcile.Release(); }
    }

    /// <summary>Synchronizes one Topic and its direct memberships after a committed domain change.</summary>
    public async Task ReconcileTopic(string roomKey, CancellationToken ct = default)
    {
        await reconcile.WaitAsync(ct);
        try
        {
            var site = await TangentSite.Get("site", ct);
            var room = await Room.Get(roomKey, ct);
            if (site is null || room is null || string.IsNullOrWhiteSpace(site.OwnerParticipantId)) return;
            using var actor = context.Bind(site.OwnerParticipantId);
            using var serviceScope = scopes.CreateScope();
            var engine = serviceScope.ServiceProvider.GetRequiredService<RoleEngine>();
            await EnsureHost(engine, site, ct);
            var tangent = await TangentCommunity.Get(room.TangentKey, ct);
            if (tangent is not null) await EnsureTangent(engine, tangent, syncMemberships: false, ct);
            await EnsureTopic(engine, room, syncMemberships: true, ct);
        }
        finally { reconcile.Release(); }
    }

    public async Task<RoomPolicy> ProjectTopic(RoomPolicy policy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(policy.ActorParticipantId)) return policy;
        using var actor = context.Bind(policy.ActorParticipantId);
        using var serviceScope = scopes.CreateScope();
        var engine = serviceScope.ServiceProvider.GetRequiredService<RoleEngine>();
        var target = TangentRoleScopes.TopicScope(policy.RoomKey);
        try
        {
            var read = policy.CanRead && await engine.Check(TangentRoleCapabilities.TopicRead, target, ct: ct);
            var write = policy.CanWrite && await engine.Check(TangentRoleCapabilities.TopicReply, target, ct: ct);
            var manage = policy.CanManage && await engine.Check(TangentRoleCapabilities.TopicManage, target, ct: ct);
            var appoint = policy.CanAppointManagers && await engine.Check(TangentRoleCapabilities.TopicAppointManagers, target, ct: ct);
            return policy with { CanRead = read, CanWrite = read && write, CanManage = manage, CanAppointManagers = appoint,
                Reason = policy.CanRead && !read ? "role-required" : policy.Reason };
        }
        catch (ScopedRoleException)
        {
            return policy with { CanRead = false, CanWrite = false, CanManage = false, CanAppointManagers = false, Reason = "role-unavailable" };
        }
    }

    private static async Task SyncTangentMembership(RoleEngine engine, TangentMembership membership, CancellationToken ct)
    {
        var desired = membership.Role switch { TangentRole.Admin => "admin", _ => null };
        await Sync(engine, TangentRoleScopes.TangentScope(membership.TangentKey), membership.ParticipantId, desired, ["admin"], ct);
    }

    private static async Task SyncRoomMembership(RoleEngine engine, RoomMembership membership, CancellationToken ct)
    {
        var desired = membership.Role switch { RoomRole.Manager => "manager", RoomRole.Member => "member", RoomRole.Reader => "reader", _ => null };
        await Sync(engine, TangentRoleScopes.TopicScope(membership.RoomKey), membership.ParticipantId, desired, ["manager", "member", "reader"], ct);
    }

    private static async Task<ScopedRoleDefinition> EnsureHost(RoleEngine engine, TangentSite site, CancellationToken ct)
    {
        await EnsureScope(engine, TangentRoleScopes.HostScope, null, ct);
        var owner = await EnsureRole(engine, TangentRoleScopes.HostScope, "owner", "Owner", TangentRoleCapabilities.All, ct);
        var participant = await EnsureRole(engine, TangentRoleScopes.HostScope, "participant", "Participant",
            [TangentRoleCapabilities.HostRead, TangentRoleCapabilities.TangentCreate, TangentRoleCapabilities.TangentRead,
             TangentRoleCapabilities.TopicCreate, TangentRoleCapabilities.TopicRead, TangentRoleCapabilities.TopicReply,
             TangentRoleCapabilities.PostEditOwn, TangentRoleCapabilities.PostDeleteOwn, TangentRoleCapabilities.PostReport], ct);
        await engine.Assign(new(TangentRoleScopes.HostScope, site.OwnerParticipantId, owner.Id,
            ScopedRolePropagation.Descendants), ct);
        return participant;
    }

    private static async Task EnsureTangent(RoleEngine engine, TangentCommunity tangent, bool syncMemberships, CancellationToken ct)
    {
        var scope = TangentRoleScopes.TangentScope(tangent.Id);
        await EnsureScope(engine, scope, TangentRoleScopes.HostScope, ct);
        var owner = await EnsureRole(engine, scope, "owner", "Owner",
            TangentRoleCapabilities.All.Where(value => value != TangentRoleCapabilities.HostManage).ToArray(), ct);
        await engine.Assign(new(scope, tangent.OwnerParticipantId, owner.Id, ScopedRolePropagation.Descendants), ct);
        await EnsureRole(engine, scope, "admin", "Admin",
            [TangentRoleCapabilities.TangentRead, TangentRoleCapabilities.TangentManage,
             TangentRoleCapabilities.TopicCreate, TangentRoleCapabilities.TopicRead, TangentRoleCapabilities.TopicReply,
             TangentRoleCapabilities.TopicManage, TangentRoleCapabilities.TopicManageParticipants,
             TangentRoleCapabilities.PostEditOwn, TangentRoleCapabilities.PostDeleteOwn,
             TangentRoleCapabilities.PostRemove, TangentRoleCapabilities.PostReport], ct);
        if (!syncMemberships) return;
        await EachTangentMembership(tangent.Id, value => SyncTangentMembership(engine, value, ct), ct);
    }

    private static async Task EnsureTopic(RoleEngine engine, Room room, bool syncMemberships, CancellationToken ct)
    {
        var scope = TangentRoleScopes.TopicScope(room.Id);
        await EnsureScope(engine, scope, TangentRoleScopes.TangentScope(room.TangentKey), ct);
        var manager = await EnsureRole(engine, scope, "manager", "Manager",
            [TangentRoleCapabilities.TopicRead, TangentRoleCapabilities.TopicReply, TangentRoleCapabilities.TopicManage,
             TangentRoleCapabilities.TopicManageParticipants, TangentRoleCapabilities.PostEditOwn,
             TangentRoleCapabilities.PostDeleteOwn, TangentRoleCapabilities.PostRemove, TangentRoleCapabilities.PostReport], ct);
        await EnsureRole(engine, scope, "member", "Member",
            [TangentRoleCapabilities.TopicRead, TangentRoleCapabilities.TopicReply, TangentRoleCapabilities.PostEditOwn,
             TangentRoleCapabilities.PostDeleteOwn, TangentRoleCapabilities.PostReport], ct);
        await EnsureRole(engine, scope, "reader", "Reader",
            [TangentRoleCapabilities.TopicRead, TangentRoleCapabilities.PostReport], ct);
        await engine.Assign(new(scope, room.CreatorParticipantId, manager.Id), ct);
        if (!syncMemberships) return;
        await EachRoomMembership(room.Id, value => SyncRoomMembership(engine, value, ct), ct);
    }

    private static async Task Sync(RoleEngine engine, ScopedRoleScopeRef scope, string subject, string? desired,
        IReadOnlyList<string> managed, CancellationToken ct)
    {
        var desiredId = desired is null ? null : RoleId(scope, desired);
        foreach (var name in managed)
        {
            var roleId = RoleId(scope, name);
            if (roleId == desiredId) continue;
            var bindingId = ScopedRoleBinding.KeyFor(scope.TenantId, subject, roleId, scope);
            if (await ScopedRoleBinding.Get(bindingId, ct) is { Revoked: false } binding)
                await engine.Revoke(binding.Id, binding.Version, ct);
        }
        if (desiredId is not null) await engine.Assign(new(scope, subject, desiredId), ct);
    }

    private static async Task EnsureScope(RoleEngine engine, ScopedRoleScopeRef scope, ScopedRoleScopeRef? parent, CancellationToken ct)
    {
        try { await engine.Register(new(scope, parent), ct); }
        catch (ScopedRoleConcurrencyException) { }
    }

    private static async Task<ScopedRoleDefinition> EnsureRole(RoleEngine engine, ScopedRoleScopeRef scope, string key,
        string name, IReadOnlyList<string> capabilities, CancellationToken ct)
    {
        var id = RoleId(scope, key);
        var current = await engine.Role(id, scope, ct);
        return current ?? await engine.Define(new(scope, name,
            capabilities.Select(value => new ScopedRoleGrantClause(value)).ToArray(), id,
            $"Built-in Tangent Space {name} role.", new Dictionary<string, string> { ["system"] = "true" }), ct);
    }

    private static string RoleId(ScopedRoleScopeRef scope, string name) => $"tangent:{scope.Type}:{scope.Id}:{name}";

    private static async Task Each<TEntity>(Func<TEntity, Task> action, CancellationToken ct)
        where TEntity : class, IEntity<string>
    {
        var sort = SortSpecParser.ParseStrict<TEntity>(nameof(IEntity<string>.Id));
        for (var page = 1; ; page++)
        {
            var rows = await Data<TEntity, string>.All(new QueryDefinition { Page = page, PageSize = BatchSize, Sort = sort }, ct);
            foreach (var row in rows) await action(row);
            if (rows.Count < BatchSize) return;
        }
    }

    private static async Task EachTangentMembership(string tangentKey, Func<TangentMembership, Task> action, CancellationToken ct)
    {
        var query = new QueryDefinition { Sort = SortSpecParser.ParseStrict<TangentMembership>(nameof(IEntity<string>.Id)) };
        for (var page = 1; ; page++)
        {
            var rows = await TangentMembership.Query(value => value.TangentKey == tangentKey,
                query.WithPagination(page, BatchSize), ct);
            foreach (var row in rows) await action(row);
            if (rows.Count < BatchSize) return;
        }
    }

    private static async Task EachRoomMembership(string roomKey, Func<RoomMembership, Task> action, CancellationToken ct)
    {
        var query = new QueryDefinition { Sort = SortSpecParser.ParseStrict<RoomMembership>(nameof(IEntity<string>.Id)) };
        for (var page = 1; ; page++)
        {
            var rows = await RoomMembership.Query(value => value.RoomKey == roomKey,
                query.WithPagination(page, BatchSize), ct);
            foreach (var row in rows) await action(row);
            if (rows.Count < BatchSize) return;
        }
    }
}
