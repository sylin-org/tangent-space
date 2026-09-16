using Koan.Data.Core.Model;

namespace Tangent.Conversation;

public sealed class ReadPosition : Entity<ReadPosition>
{
    public string ParticipantId { get; set; } = "";
    public string TopicKey { get; set; } = "";
    public long Sequence { get; set; }
    public DateTimeOffset AcknowledgedAt { get; set; }
    public static string Key(string did, string topic) => SourceDecision.Hash(did + "\n" + topic);
}
