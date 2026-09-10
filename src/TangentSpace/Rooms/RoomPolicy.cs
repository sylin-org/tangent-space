namespace TangentSpace.Rooms;

/// <summary>A current decision, not a durable grant. Acceptance must execute inside the coordinator callback.</summary>
public sealed record RoomPolicy(
    string RoomKey,
    string? ActorDid,
    long SelectedPolicyRevision,
    long SitePolicyRevision,
    RoomAdmission Admission,
    RoomSpaceState SpaceState,
    string? SpaceUri,
    RoomRole? Role,
    bool IsOwner,
    bool CanRead,
    bool CanWrite,
    bool CanManage,
    bool CanAppointManagers,
    string Reason);
