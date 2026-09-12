using Koan.Data.Core.Model;

namespace TangentSpace.Rooms;

public sealed class RoomAudit : Entity<RoomAudit>
{
    public string ActorParticipantId { get; set; } = "";
    public string RoomKey { get; set; } = "";
    public string? TargetParticipantId { get; set; }
    public RoomAdministration Operation { get; set; }
    public RoomRole? RequestedRole { get; set; }
    public bool Accepted { get; set; }
    public RoomDenial? Denial { get; set; }
    public string Reason { get; set; } = "";
    public long SelectedPolicyRevision { get; set; }
    public long SitePolicyRevision { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
