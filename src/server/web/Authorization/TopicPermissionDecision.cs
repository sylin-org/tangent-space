namespace TangentSpace.Authorization;

/// <summary>One typed permission decision over the current effective TopicPolicy.</summary>
/// <param name="Capability">The requested capability.</param>
/// <param name="Allowed">Whether the current policy grants the capability.</param>
/// <param name="Reason">Stable machine-readable reason; <c>allowed</c> when granted.</param>
/// <param name="PolicyRevision">The TopicPolicy revision the decision was evaluated against.</param>
public sealed record TopicPermissionDecision(TopicCapability Capability, bool Allowed, string Reason, long PolicyRevision);
