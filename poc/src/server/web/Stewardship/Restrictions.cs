using Koan.Data.Core;

namespace Tangent.Stewardship;

/// <summary>Expiry is checked here at policy-evaluation time; no scheduler clears restrictions.</summary>
public static class Restrictions
{
    /// <summary>Pure decision: a timeout stops restricting the instant its window passes; a lift is not a restriction.</summary>
    public static EffectiveRestriction? Evaluate(ScopedRestriction? stored, DateTimeOffset now)
    {
        if (stored is null || !Enum.IsDefined(stored.Kind)) return null;
        if (stored.Kind == RestrictionKind.Ban) return new EffectiveRestriction(Banned: true);
        return stored.Kind == RestrictionKind.Timeout && stored.Until is { } until && until > now
            ? new EffectiveRestriction(Banned: false, stored.BlockPosting, stored.BlockStarting)
            : null;
    }

    /// <summary>Combines scopes for one topic: a topic record never erases an inherited Tangent ban.</summary>
    public static EffectiveRestriction? Combine(EffectiveRestriction? topic, EffectiveRestriction? tangent)
        => tangent is { Banned: true } ? tangent : topic is { Banned: true } ? topic : topic is null ? tangent : tangent is null ? topic : new EffectiveRestriction(false,topic.BlockPosting || tangent.BlockPosting,topic.BlockStarting || tangent.BlockStarting);

    /// <summary>Restriction effective for a participant in one topic: the topic record combined with its Tangent's record.</summary>
    public static async Task<EffectiveRestriction?> ForTopic(string did, string topicKey, string tangentKey, DateTimeOffset now, CancellationToken ct)
    {
        var topicScoped = Evaluate(await ScopedRestriction.Get(ScopedRestriction.Key(RestrictionScope.Topic, topicKey, did), ct), now);
        var tangentScoped = await ForTangent(did, tangentKey, now, ct);
        return Combine(topicScoped, tangentScoped);
    }

    public static async Task<EffectiveRestriction?> ForTangent(string did, string tangentKey, DateTimeOffset now, CancellationToken ct)
        => Evaluate(await ScopedRestriction.Get(ScopedRestriction.Key(RestrictionScope.Tangent, tangentKey, did), ct), now);
}
