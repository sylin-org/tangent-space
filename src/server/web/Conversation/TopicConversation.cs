using Koan.Data.Core.Model;

namespace TangentSpace.Conversation;

public sealed class TopicConversation : Entity<TopicConversation>
{
    public long LastSequence { get; set; }
}
