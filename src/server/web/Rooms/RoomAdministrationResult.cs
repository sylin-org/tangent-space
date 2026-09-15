namespace TangentSpace.Rooms;

public sealed record RoomAdministrationResult(
    bool Accepted,
    RoomDenial? Denial,
    string Reason,
    string RoomKey,
    long PolicyRevision,
    string AuditId);
