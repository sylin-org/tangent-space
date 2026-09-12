using CarpaNet.Identity;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace TangentSpace.Participants;

/// <summary>One entry in a participant's identity collection. Exact-value lookup is one query
/// on the composite key (kind + separator + value). The internal identity value is derived from
/// the participant id and minted at creation; it is never written independently.</summary>
[Index(Fields = [nameof(ParticipantId), nameof(Id)])]
public sealed class ParticipantIdentity : Entity<ParticipantIdentity>
{
    public const string AtprotoKind = "atproto";
    public const string InternalKind = "internal";
    public const string ConnectorClientKind = "connector-client";
    public const string Separator = "\u0001";

    public string Kind { get; set; } = "";
    public string Value { get; set; } = "";
    /// <summary>Current label for the identity: the handle for atproto and connector-created
    /// local entries; null for internal entries.</summary>
    public string? Label { get; set; }
    public string ParticipantId { get; set; } = "";
    public DateTimeOffset AddedAt { get; set; }

    public static string Key(string kind, string value) => kind + Separator + value;
    public static string AtprotoKey(string did) => Key(AtprotoKind, did);
    /// <summary>The internal identity is derived: its value always equals this form.</summary>
    public static string InternalValue(string participantId) => ParticipantLookup.LocalPrefix + participantId;
    public static string InternalKey(string participantId) => Key(InternalKind, InternalValue(participantId));

    public static ParticipantIdentity Atproto(string participantId, string verifiedDid, string? verifiedHandle, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedDid);
        if (!IdentityResolver.IsValidDid(verifiedDid))
            throw new ArgumentException("An atproto identity needs a verified AT DID.", nameof(verifiedDid));
        if (!Participant.IsValidId(participantId))
            throw new ArgumentException("An atproto identity needs a valid participant id.", nameof(participantId));
        return new() { Id = AtprotoKey(verifiedDid), Kind = AtprotoKind, Value = verifiedDid,
            Label = verifiedHandle, ParticipantId = participantId, AddedAt = now };
    }

    public static ParticipantIdentity Internal(string participantId, DateTimeOffset now)
    {
        if (!Participant.IsValidId(participantId))
            throw new ArgumentException("An internal identity needs a valid participant id.", nameof(participantId));
        return new() { Id = InternalKey(participantId), Kind = InternalKind, Value = InternalValue(participantId),
            Label = null, ParticipantId = participantId, AddedAt = now };
    }

    /// <summary>Refreshes the current label of an existing entry; the value never changes.</summary>
    public void Relabel(string? label) => Label = label;
}
