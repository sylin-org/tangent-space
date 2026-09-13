using Koan.Web.Authorization;

namespace TangentSpace.Rooms.Web;

/// <summary>Koan-native row visibility for the anonymous Topic read surface.</summary>
public sealed class PublicTopicAccess : EntityAccess<Room>
{
    protected override ActionGate ReadGate => Gate.Anyone;
    protected override ActionGate WriteGate => Gate.HasClaim("tangent:surface", "internal-domain-command");
    protected override ActionGate RemoveGate => Gate.HasClaim("tangent:surface", "internal-domain-command");

    public override IAccessFilter<Room> Constrain(IAccessFilter<Room> q, AccessAction action)
        => action == AccessAction.Read
            ? q.Where(room => room.ReadAudience == RoomReadAudience.Public
                && (room.SpaceState == RoomSpaceState.Local
                    || room.SpaceState == RoomSpaceState.Ready && room.SpaceUri != null && room.SpaceUri != ""))
            : action is AccessAction.Update or AccessAction.Delete ? q.Where(room => false) : q;
}
