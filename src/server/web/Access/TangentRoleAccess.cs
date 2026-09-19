using Koan.Data.Core;
using Koan.Identity.Roles;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Spaces;
using Tangent.Community;
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

    public async Task<TopicPolicy> ProjectTopic(Topic topic, Community.Tangent? tangent, TopicPolicy selected,
        bool suspended, EffectiveRestriction? restriction, ParticipantClassification classification,
        CancellationToken ct = default)
        => AccessEvaluator.Decide(topic, tangent, selected, await Bag(selected.ActorParticipantId, ct),
            suspended, restriction, classification);

    public async Task Seed(Space? space, CancellationToken ct = default)
    {
        foreach (var seed in TangentBuiltInRoles.Defaults)
        {
            var current = await roles.Get(seed.Token, ct);
            if (current is null || seed.Key == TangentBuiltInRoles.OwnerKey)
                await roles.Define(seed.Token, seed.Name, seed.Permissions, Metadata(seed), ct);
        }
    }

    internal static IReadOnlyDictionary<string, string> Metadata(TangentRoleSeed seed)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["purpose"] = seed.Description,
            ["color"] = seed.Color,
            ["system"] = seed.Key == TangentBuiltInRoles.OwnerKey ? "true" : "false",
            ["selfEnrollable"] = seed.IsSelfEnrollable ? "true" : "false"
        };
}

/// <summary>
/// The Owner role holds no members: the owner's authority derives from the Space at the bag
/// (ADR 0013). The role exists as a definition - its display and its permissions - and this is
/// its rule: the definition stays built-in and the role is never deleted. Every other change is
/// refused.
/// </summary>
public static class TangentOwnerRoleGuard
{
    private static int registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) != 0) return;
        var lifecycle = new RoleLifecycleBuilder();
        lifecycle.RoleChanging(ChangingDefinition).PermissionsChanging(ChangingDefinition).RoleDeleting(Deleting);
    }

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

    private static RoleChangeDecision Refused()
        => RoleChangeDecision.Veto("tangent.owner_role.protected", "The Owner role follows the server's accountable human owner.");
}
