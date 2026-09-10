using Koan.Data.Core.Model;

namespace TangentSpace.Conversation;

public sealed class RoomConversation : Entity<RoomConversation>
{
    public long LastSequence { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? LastCompleteAt { get; set; }
    public string Freshness { get; set; } = "not-yet-checked";
    public string? ReposCursor { get; set; }
    public int ParticipantPage { get; set; } = 1;
}
