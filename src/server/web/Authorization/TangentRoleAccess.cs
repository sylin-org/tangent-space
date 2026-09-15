using Koan.Identity.Roles;
using TangentSpace.Participants;
using TangentSpace.Site;
using TangentSpace.Communities;
using TangentSpace.Rooms;

namespace TangentSpace.Authorization;

/// <summary>
/// Tangent's thin consumer of Koan roles. Resource access stays on the resource;
/// Koan compiles the participant bag and performs the token intersection.
/// </summary>
public sealed class TangentRoleAccess(RoleCollection roles)
{
    public Task<RoleBag> Bag(string? participantId, CancellationToken ct = default)
        => roles.Bag(participantId, Participant.IsValidId(participantId), ct);

    public static bool CanDo(IEnumerable<string>? tokens, RoleBag bag)
    {
        var criteria = PermissionCriteria.Any((tokens ?? []).ToArray());
        return Koan.Identity.Roles.Role.CanDo(criteria, bag);
    }

    public async Task<RoomPolicy> ProjectTopic(Room room, TangentCommunity? tangent, RoomPolicy selected,
        bool suspended, EffectiveRestriction? restriction, ParticipantClassification classification,
        CancellationToken ct = default)
    {
        var bag = await Bag(selected.ActorParticipantId, ct);
        var effective = (room.Access ?? AccessMap.TopicDefaults())
            .ResolveTopic(tangent?.Access ?? AccessMap.TangentDefaults());
        var ownsResource = selected.IsOwner
            || string.Equals(room.CreatorParticipantId, selected.ActorParticipantId, StringComparison.Ordinal);
        var siteUnavailable = selected.SitePolicyRevision <= 0;
        var missingParent = tangent is null;
        var ready = room.SpaceState == RoomSpaceState.Local
            || room.SpaceState == RoomSpaceState.Ready && !string.IsNullOrWhiteSpace(room.SpaceUri);
        var banned = restriction is { Banned: true } && !ownsResource;
        var timedOut = restriction is { Banned: false } && !ownsResource;
        var rights = tangent?.ParticipationRights(classification) ?? (Read: false, Write: false);
        var read = !siteUnavailable && !suspended && !missingParent && ready && !banned && rights.Read
            && (ownsResource || CanDo(effective.See, bag));
        var write = read && !timedOut && !room.IsLocked && rights.Write
            && (ownsResource || CanDo(effective.Post, bag));
        var manage = !siteUnavailable && !suspended && !missingParent && !banned && !timedOut
            && (ownsResource || CanDo(effective.Manage, bag));
        var reason = siteUnavailable ? "site-unavailable" : missingParent ? "tangent-not-found" : suspended ? "suspended" : banned ? "banned"
            : !ready ? "space-pending" : !read ? "role-required" : "allowed";
        return selected with
        {
            IsOwner = ownsResource,
            CanRead = read,
            CanWrite = write,
            CanManage = manage,
            CanAppointManagers = manage,
            Reason = reason
        };
    }

    public async Task Seed(TangentSite? site, CancellationToken ct = default)
    {
        foreach (var seed in TangentBuiltInRoles.Defaults)
        {
            var current = await roles.Get(seed.Token, ct);
            if (current is null || seed.Key == TangentBuiltInRoles.OwnerKey)
                await roles.Define(seed.Token, seed.Name, seed.Permissions, Metadata(seed), ct);
        }

        if (site is not null && Participant.IsValidId(site.OwnerParticipantId))
            await EnsureOwner(site.OwnerParticipantId, ct);
    }

    public async Task EnsureOwner(string participantId, CancellationToken ct = default)
    {
        if (!Participant.IsValidId(participantId))
            throw new ArgumentException("The server owner must be an enrolled participant.", nameof(participantId));
        var owner = await roles.Get(TangentBuiltInRoles.Owner.Token, ct)
            ?? await roles.Define(TangentBuiltInRoles.Owner.Token, TangentBuiltInRoles.Owner.Name,
                TangentBuiltInRoles.Owner.Permissions, Metadata(TangentBuiltInRoles.Owner), ct);
        foreach (var other in owner.Members.Where(member => member != participantId).ToArray())
            await roles.Remove(owner.Id, other, ct);
        await roles.Add(owner.Id, participantId, ct);
    }

    private static IReadOnlyDictionary<string, string> Metadata(TangentRoleSeed seed)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["purpose"] = seed.Description,
            ["color"] = seed.Color,
            ["system"] = seed.Key == TangentBuiltInRoles.OwnerKey ? "true" : "false"
        };
}

/// <summary>
/// Keeps the one human-accountability role outside the generic editor. Application
/// bootstrap may repair it without an HTTP actor; browser/API mutations may not.
/// </summary>
public static class TangentOwnerRoleGuard
{
    private static int registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0) return;
        var lifecycle = new RoleLifecycleBuilder();
        lifecycle.MemberAdding(Protect).MemberRemoving(Protect).PermissionsChanging(Protect)
            .RoleChanging(Protect).RoleDeleting(Protect);
    }

    private static ValueTask<RoleChangeDecision> Protect(RoleChangeContext context)
        => ValueTask.FromResult(context.Previous.Id == TangentBuiltInRoles.Owner.Token && context.Actor is not null
            ? RoleChangeDecision.Veto("tangent.owner_role.protected", "The Owner role follows the server's accountable human owner.")
            : RoleChangeDecision.Continue());
}
