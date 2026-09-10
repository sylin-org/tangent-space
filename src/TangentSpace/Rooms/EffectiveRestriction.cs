namespace TangentSpace.Rooms;

/// <summary>An expiry-filtered decision: timeouts whose window has passed simply vanish at evaluation time.</summary>
public sealed record EffectiveRestriction(bool Banned);
