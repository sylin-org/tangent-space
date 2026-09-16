using Koan.Data.Core.Model;
using Tangent.Community;

namespace Tangent.Stewardship;

public sealed class TopicAudit : Entity<TopicAudit>
{
    public string ActorParticipantId { get; set; } = "";
    public string RoomKey { get; set; } = "";
    public string? TargetParticipantId { get; set; }
    public TopicAdministration Operation { get; set; }
    public TopicRole? RequestedRole { get; set; }
    public TopicReadAudience? RequestedReadAudience { get; set; }
    public bool? PublishExistingHistory { get; set; }
    public bool Accepted { get; set; }
    public TopicDenial? Denial { get; set; }
    public string Reason { get; set; } = "";
    public long SelectedPolicyRevision { get; set; }
    public long SpacePolicyRevision { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
