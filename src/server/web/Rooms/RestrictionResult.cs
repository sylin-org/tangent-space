namespace TangentSpace.Rooms;

/// <summary>The stored scoped-restriction state after one audited change (or inspection).</summary>
public sealed record RestrictionResult(RestrictionScope Scope, string ScopeKey, string ParticipantId,
    RestrictionKind Kind, DateTimeOffset? Until, string Reason, string AuditId);
