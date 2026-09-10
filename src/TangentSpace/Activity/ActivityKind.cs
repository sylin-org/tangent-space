namespace TangentSpace.Activity;

// Journal entries are delivery markers. Conversation content is always read through its own room-history API.
public enum ActivityKind
{
    MessageAccepted,
    SourceFreshnessChanged,
    ReadAcknowledged,
    RoomChanged,
    MembershipChanged,
    ParticipantChanged,
    TangentChanged,
    InvitationChanged
}
