using Koan.Data.Core;

namespace TangentSpace.Rooms;

/// <summary>Expiry is checked here at policy-evaluation time; no scheduler clears restrictions.</summary>
public static class Restrictions
{
    /// <summary>Pure decision: a timeout stops restricting the instant its window passes; a lift is not a restriction.</summary>
    public static EffectiveRestriction? Evaluate(ScopedRestriction? stored, DateTimeOffset now)
    {
        if (stored is null || !Enum.IsDefined(stored.Kind)) return null;
        if (stored.Kind == RestrictionKind.Ban) return new EffectiveRestriction(Banned: true);
        return stored.Kind == RestrictionKind.Timeout && stored.Until is { } until && until > now
            ? new EffectiveRestriction(Banned: false)
            : null;
    }

    /// <summary>Combines scopes for one room: a channel record never erases an inherited Tangent ban.</summary>
    public static EffectiveRestriction? Combine(EffectiveRestriction? channel, EffectiveRestriction? tangent)
        => tangent is { Banned: true } ? tangent : channel is { Banned: true } ? channel : channel ?? tangent;

    /// <summary>Restriction effective for a participant in one room: the channel record combined with its Tangent's record.</summary>
    public static async Task<EffectiveRestriction?> ForRoom(string did, string roomKey, string tangentKey, DateTimeOffset now, CancellationToken ct)
    {
        var roomScoped = Evaluate(await ScopedRestriction.Get(ScopedRestriction.Key(RestrictionScope.Room, roomKey, did), ct), now);
        var tangentScoped = await ForTangent(did, tangentKey, now, ct);
        return Combine(roomScoped, tangentScoped);
    }

    public static async Task<EffectiveRestriction?> ForTangent(string did, string tangentKey, DateTimeOffset now, CancellationToken ct)
        => Evaluate(await ScopedRestriction.Get(ScopedRestriction.Key(RestrictionScope.Tangent, tangentKey, did), ct), now);
}
