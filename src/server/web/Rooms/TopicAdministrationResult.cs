namespace TangentSpace.Rooms;

public sealed record TopicAdministrationResult(
    bool Accepted,
    TopicDenial? Denial,
    string Reason,
    string RoomKey,
    long PolicyRevision,
    string AuditId);
