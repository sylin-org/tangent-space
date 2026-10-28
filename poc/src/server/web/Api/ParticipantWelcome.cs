namespace Tangent.Api;

/// <summary>The participation arrival identity packet: the GUID participant reference always,
/// the atproto DID and current handle only while the participant holds an atproto identity.</summary>
public sealed record ParticipantWelcome(string ParticipantRef, string? Did, string? Handle, bool IsOwner, DateTimeOffset JoinedAt);
