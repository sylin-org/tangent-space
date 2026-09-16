namespace Tangent.Activity;

// Journal entries are delivery markers. Conversation content is always read through its own topic-history API.
public enum ActivityKind
{
    PostAccepted,
    PostEdited,
    PostDeleted,
    ReadAcknowledged,
    TopicChanged,
    MembershipChanged,
    ParticipantChanged,
    TangentChanged,
    InvitationChanged,
    RestrictionChanged
}
