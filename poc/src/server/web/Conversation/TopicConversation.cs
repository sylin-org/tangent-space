using Koan.Data.Core.Model;

namespace Tangent.Conversation;

public sealed class TopicConversation : Entity<TopicConversation>
{
    public long LastSequence { get; set; }
}
