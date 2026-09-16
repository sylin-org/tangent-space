namespace TangentSpace.Rooms;

public sealed record RoomDescription(string Key, string Title, string Topic, string CreatorParticipantId,
    RoomAdmission Admission, long PolicyRevision, long SpacePolicyRevision,
    bool CanRead, bool CanWrite, bool CanManage, bool CanAppointManagers, string AccessState, string TangentKey,
    bool MembersOnly = false, bool AllowPostEditing = false, bool IsLocked = false,
    TangentSpace.Authorization.PermissionView? Permissions = null,
    RoomReadAudience ReadAudience = RoomReadAudience.Restricted)
{
    internal static RoomDescription From(Room room, RoomPolicy policy)
        => new(room.Id, room.Title, room.Topic, room.CreatorParticipantId, room.Admission,
            policy.SelectedPolicyRevision, policy.SpacePolicyRevision, policy.CanRead, policy.CanWrite,
            policy.CanManage, policy.CanAppointManagers, policy.Reason, room.TangentKey, room.MembersOnly,
            room.AllowPostEditing, room.IsLocked, TangentSpace.Authorization.Permissions.Topic(policy), room.ReadAudience);
}
