using Koan.Data.Core;
using Koan.Identity.Roles;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Spaces;
using Tangent.Community;
using Tangent.Stewardship;

namespace Tangent.Access;

/// <summary>
/// Tangent's thin consumer of Koan roles, and the one seam where authority tokens are compiled.
/// Koan compiles the stored memberships; this adapter adds what is derived, never stored (ADR 0013):
/// `everyone` and `authenticated` come from the framework, and the Space's owner holds the Owner
/// role's tokens by being the Space's owner - no membership row records it.
/// </summary>
public sealed class TangentRoleAccess(RoleCollection roles)
{
    /// <summary>The actor's authority tokens: Koan's compiled bag plus the owner derivation.</summary>
    public async Task<IReadOnlyCollection<string>> Bag(string? participantId, CancellationToken ct = default)
    {
        var currentSpace = await Space.Get(TangentConstants.SpaceId, ct);
        if (currentSpace is not null && participantId is not null && !await SpaceAdmission.Admitted(currentSpace, participantId, ct)) return [AccessTokens.Everyone];
        var bag = await roles.Bag(participantId, Participant.IsValidId(participantId), ct);
        var tokens = bag.Tokens.ToList();
        if (Participant.IsValidId(participantId))
        {
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            if (space?.IsOwner(participantId) == true)
            {
                tokens.Add(TangentBuiltInRoles.Owner.Token);
                tokens.AddRange(TangentBuiltInRoles.Owner.Permissions);
            }
        }
        return tokens;
    }

    /// <summary>Participant stewardship requires a current grant, active identity and admission.</summary>
    public async Task<bool> CanManageParticipants(string? participantId, CancellationToken ct = default)
    {
        using var fresh = EntityContext.NoCache();
        if (participantId is null || await Participant.Get(participantId, ct) is not { IsSuspended: false }) return false;
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        return await SpaceAdmission.Admitted(space, participantId, ct)
            && CanDo([TangentPermissions.ManageParticipants], await Bag(participantId, ct));
    }

    /// <summary>Any-of match with the derived built-in roles resolved at the one adapter:
    /// `role:everyone` matches every visitor and `role:participant` everyone signed in, because
    /// neither is stored (ADR 0013). The bare `everyone` and `authenticated` tokens the framework
    /// mints keep matching as they always did.</summary>
    public static bool CanDo(IEnumerable<string>? tokens, IReadOnlyCollection<string> bagTokens)
        => (tokens ?? []).Select(ResolveDerived).Any(bagTokens.Contains);

    public static string ResolveDerived(string token) => token switch
    {
        "role:everyone" => AccessTokens.Everyone,
        "role:participant" => AccessTokens.Authenticated,
        _ => token
    };

    public static IReadOnlyCollection<string> InTangent(IReadOnlyCollection<string> bag, TangentMembership? membership)
        => bag.Contains(AccessTokens.Authenticated) && membership?.Role is TangentRole.Member or TangentRole.Admin or TangentRole.Reader
            ? bag.Append(AccessTokens.TangentMember).ToArray() : bag;

    public async Task<TopicPolicy> ProjectTopic(Topic topic, Community.Tangent? tangent, TopicPolicy selected,
        bool suspended, EffectiveRestriction? restriction, ParticipantClassification classification,
        CancellationToken ct = default)
    {
        var space=await Space.Get(TangentConstants.SpaceId,ct);
        if(selected.ActorParticipantId is not null && !await SpaceAdmission.Admitted(space,selected.ActorParticipantId,ct))
            return selected with { CanRead = !suspended && restriction is not { Banned: true } && topic.IsPubliclyReadable(tangent), CanWrite=false, CanManage=false, Reason="join-space" };
        var membership = selected.ActorParticipantId is null || tangent is null ? null
            : await TangentMembership.Get(TangentMembership.Key(tangent.Id, selected.ActorParticipantId), ct);
        var bag = InTangent(await Bag(selected.ActorParticipantId, ct), membership);
        if (topic.IsPubliclyReadable(tangent) && !suspended && restriction is not { Banned: true }
            && selected.Role != TopicRole.Removed && membership?.Role != TangentRole.Removed)
            selected = selected with { CanRead = true, Reason = "allowed" };
        if (topic.MembersOnly && !selected.IsOwner && topic.CreatorParticipantId != selected.ActorParticipantId
            && membership?.Role is not (TangentRole.Member or TangentRole.Admin or TangentRole.Reader)
            && selected.Role is not (TopicRole.Member or TopicRole.Manager or TopicRole.Reader)
            && !CanDo([TangentPermissions.ReadTopics], bag))
            selected = selected with { CanRead = false, CanWrite = false, CanManage = false, Reason = "community-membership-required" };
        return AccessEvaluator.Decide(topic,tangent,selected,bag,suspended,restriction,classification);
    }

    public async Task Seed(Space? space, CancellationToken ct = default)
    {
        foreach (var seed in TangentBuiltInRoles.Defaults)
        {
            var current = await roles.Get(seed.Token, ct);
            if (current is null || TangentDerivedRoleGuard.IsDerived(seed.Token))
                await roles.Define(seed.Token, seed.Name, seed.Permissions, Metadata(seed), ct);
        }
    }

    internal static IReadOnlyDictionary<string, string> Metadata(TangentRoleSeed seed)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["purpose"] = seed.Description,
            ["color"] = seed.Color,
            ["system"] = TangentDerivedRoleGuard.IsDerived(seed.Token) ? "true" : "false",
            ["selfEnrollable"] = seed.IsSelfEnrollable ? "true" : "false"
        };
}

/// <summary>
/// Derived roles follow visitor authentication and Space ownership (ADR 0013). Their definitions
/// can be seeded, but their memberships cannot be edited or removed through role mutations.
/// </summary>
public static class TangentDerivedRoleGuard
{
    private static int registered;

    public static bool IsDerived(string roleId)
        => roleId == TangentBuiltInRoles.Owner.Token
            || roleId == TangentBuiltInRoles.Participant.Token
            || roleId == "role:everyone";

    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0) return;
        var lifecycle = new RoleLifecycleBuilder();
        lifecycle.MemberAdding(ChangingMembership).MemberRemoving(ChangingMembership)
            .RoleChanging(ChangingDefinition).PermissionsChanging(ChangingDefinition).RoleDeleting(Deleting);
    }

    private static ValueTask<RoleChangeDecision> ChangingDefinition(RoleChangeContext context)
        => ValueTask.FromResult(context.Previous.Id switch
        {
            var id when id == TangentBuiltInRoles.Owner.Token && IsBuiltIn(context.Current, TangentBuiltInRoles.Owner)
                => RoleChangeDecision.Continue(),
            var id when id == TangentBuiltInRoles.Participant.Token && IsBuiltIn(context.Current, TangentBuiltInRoles.Participant)
                => RoleChangeDecision.Continue(),
            var id when IsDerived(id) => Refused(),
            _ => RoleChangeDecision.Continue()
        });

    private static ValueTask<RoleChangeDecision> ChangingMembership(RoleChangeContext context)
        => ValueTask.FromResult(IsDerived(context.Previous.Id) ? Refused() : RoleChangeDecision.Continue());

    private static ValueTask<RoleChangeDecision> Deleting(RoleChangeContext context)
        => ValueTask.FromResult(IsDerived(context.Previous.Id) ? Refused() : RoleChangeDecision.Continue());

    private static bool IsBuiltIn(Koan.Identity.Roles.Role role, TangentRoleSeed seed)
    {
        var metadata = TangentRoleAccess.Metadata(seed);
        return role.Name == seed.Name
            && role.Permissions.ToHashSet(StringComparer.Ordinal).SetEquals(seed.Permissions)
            && role.Metadata.Count == metadata.Count
            && metadata.All(entry => role.Metadata.TryGetValue(entry.Key, out var value) && value == entry.Value);
    }

    private static RoleChangeDecision Refused()
        => RoleChangeDecision.Veto("tangent.derived_role.protected", "This role follows participant identity and cannot be assigned or changed here.");
}
