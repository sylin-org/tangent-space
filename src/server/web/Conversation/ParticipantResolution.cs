namespace TangentSpace.Conversation;

/// <summary>Read-time label resolution for one participant (ADR 0008): the facet binds the
/// DID; this carries the fresh handle and classification resolved at serve time.</summary>
public sealed record ParticipantResolution(string? Handle, string? DisplayName, string Classification);
