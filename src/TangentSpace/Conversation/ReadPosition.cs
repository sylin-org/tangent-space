using Koan.Data.Core.Model;

namespace TangentSpace.Conversation;

public sealed class ReadPosition : Entity<ReadPosition>
{
    public string ParticipantDid { get; set; } = "";
    public string RoomKey { get; set; } = "";
    public long Sequence { get; set; }
    public DateTimeOffset AcknowledgedAt { get; set; }
    public static string Key(string did, string room) => SourceDecision.Hash(did + "\n" + room);
}
