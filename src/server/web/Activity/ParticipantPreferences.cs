using Koan.Data.Core.Model;

namespace Tangent.Activity;

public sealed class ParticipantPreferences : Entity<ParticipantPreferences>
{
    public string Theme { get; set; } = "system";
    public DateTimeOffset NotificationsReadAt { get; set; }
    public bool NotifyReplies { get; set; } = true;
    public bool NotifyMentions { get; set; } = true;
    public string RepliesPlacement { get; set; } = "beside";
    public bool NotifyNewPeople { get; set; } = true;
    public bool NotifyFollowing { get; set; } = true;
}
