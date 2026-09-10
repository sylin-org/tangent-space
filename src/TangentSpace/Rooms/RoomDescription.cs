namespace TangentSpace.Rooms;

public sealed record RoomDescription(string Key, string Title, string Topic, string CreatorOwnerDid,
    RoomAdmission Admission, RoomSpaceState SpaceState, string? SpaceUri, long PolicyRevision, long SitePolicyRevision,
    bool CanRead, bool CanWrite, bool CanManage, bool CanAppointManagers, string AccessState, string TangentKey = "home")
{
    internal static RoomDescription From(Room room, RoomPolicy policy)
        => new(room.Id, room.Title, room.Topic, room.CreatorOwnerDid, room.Admission, room.SpaceState, room.SpaceUri,
            policy.SelectedPolicyRevision, policy.SitePolicyRevision, policy.CanRead, policy.CanWrite,
            policy.CanManage, policy.CanAppointManagers, policy.Reason, room.TangentKey);
}
