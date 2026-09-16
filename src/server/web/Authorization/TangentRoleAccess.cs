using Koan.Data.Core;
using Koan.Identity.Roles;
using TangentSpace.Infrastructure;
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

    public async Task<TopicPolicy> ProjectTopic(Topic topic, Tangent? tangent, TopicPolicy selected,
        bool suspended, EffectiveRestriction? restriction, ParticipantClassification classification,
        CancellationToken ct = default)
    {
        var bag = await Bag(selected.ActorParticipantId, ct);
        var effective = (topic.Access ?? AccessMap.TopicDefaults())
            .ResolveTopic(tangent?.Access ?? AccessMap.TangentDefaults());
        var ownsResource = selected.IsOwner
            || string.Equals(topic.CreatorParticipantId, selected.ActorParticipantId, StringComparison.Ordinal);
        var siteUnavailable = selected.SpacePolicyRevision <= 0;
        var missingParent = tangent is null;
        var banned = restriction is { Banned: true } && !ownsResource;
        var timedOut = restriction is { Banned: false } && !ownsResource;
        var rights = tangent?.ParticipationRights(classification) ?? (Read: false, Write: false);
        var read = !siteUnavailable && !suspended && !missingParent && !banned && rights.Read
            && (ownsResource || CanDo(effective.See, bag));
        var write = read && !timedOut && !topic.IsLocked && rights.Write
            && (ownsResource || CanDo(effective.Post, bag));
        var manage = !siteUnavailable && !suspended && !missingParent && !banned && !timedOut
            && (ownsResource || CanDo(effective.Manage, bag));
        var reason = siteUnavailable ? "site-unavailable" : missingParent ? "tangent-not-found" : suspended ? "suspended" : banned ? "banned"
            : !read ? "role-required" : "allowed";
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

    public async Task Seed(Space? space, CancellationToken ct = default)
    {
        foreach (var seed in TangentBuiltInRoles.Defaults)
        {
            var current = await roles.Get(seed.Token, ct);
            if (current is null || seed.Key == TangentBuiltInRoles.OwnerKey)
                await roles.Define(seed.Token, seed.Name, seed.Permissions, Metadata(seed), ct);
        }

        if (space is not null && Participant.IsValidId(space.OwnerParticipantId))
            await EnsureOwner(space.OwnerParticipantId, ct);
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

    internal static IReadOnlyDictionary<string, string> Metadata(TangentRoleSeed seed)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["purpose"] = seed.Description,
            ["color"] = seed.Color,
            ["system"] = seed.Key == TangentBuiltInRoles.OwnerKey ? "true" : "false"
        };
}

/// <summary>
/// The Owner role is a stored projection of the Host: its only member is the space's
/// accountable human owner and its definition is the built-in one. Koan role bags cannot take
/// application-derived tokens, so the projection is stored, and this is its rule. A change that
/// keeps to it passes whoever makes it (the owner's own claim, startup's repair); every other
/// change is refused.
/// </summary>
public static class TangentOwnerRoleGuard
{
    private static int registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0) return;
        var lifecycle = new RoleLifecycleBuilder();
        lifecycle.MemberAdding(AddingMember).MemberRemoving(RemovingMember)
            .RoleChanging(ChangingDefinition).PermissionsChanging(ChangingDefinition).RoleDeleting(Deleting);
    }

    private static async ValueTask<RoleChangeDecision> AddingMember(RoleChangeContext context)
        => !IsOwnerRole(context) || context.Subject == await SiteOwner(context.CancellationToken)
            ? RoleChangeDecision.Continue() : Refused();

    private static async ValueTask<RoleChangeDecision> RemovingMember(RoleChangeContext context)
        => !IsOwnerRole(context) || context.Subject != await SiteOwner(context.CancellationToken)
            ? RoleChangeDecision.Continue() : Refused();

    private static ValueTask<RoleChangeDecision> ChangingDefinition(RoleChangeContext context)
        => ValueTask.FromResult(!IsOwnerRole(context) || IsBuiltIn(context.Current) ? RoleChangeDecision.Continue() : Refused());

    private static ValueTask<RoleChangeDecision> Deleting(RoleChangeContext context)
        => ValueTask.FromResult(IsOwnerRole(context) ? Refused() : RoleChangeDecision.Continue());

    private static bool IsOwnerRole(RoleChangeContext context) => context.Previous.Id == TangentBuiltInRoles.Owner.Token;

    private static bool IsBuiltIn(Koan.Identity.Roles.Role role)
    {
        var metadata = TangentRoleAccess.Metadata(TangentBuiltInRoles.Owner);
        return role.Name == TangentBuiltInRoles.Owner.Name
            && role.Permissions.ToHashSet(StringComparer.Ordinal).SetEquals(TangentBuiltInRoles.Owner.Permissions)
            && role.Metadata.Count == metadata.Count
            && metadata.All(entry => role.Metadata.TryGetValue(entry.Key, out var value) && value == entry.Value);
    }

    private static async Task<string?> SiteOwner(CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        return (await Space.Get(TangentConstants.SpaceId, ct))?.OwnerParticipantId;
    }

    private static RoleChangeDecision Refused()
        => RoleChangeDecision.Veto("tangent.owner_role.protected", "The Owner role follows the server's accountable human owner.");
}
