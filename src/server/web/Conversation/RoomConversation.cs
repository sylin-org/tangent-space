using Koan.Data.Core.Model;

namespace TangentSpace.Conversation;

public sealed class RoomConversation : Entity<RoomConversation>
{
    public long LastSequence { get; set; }
}
