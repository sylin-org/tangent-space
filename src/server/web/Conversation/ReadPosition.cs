using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace Tangent.Conversation;

public sealed class ReadPosition : Entity<ReadPosition>
{
    public string ParticipantId { get; set; } = "";
    public string TopicKey { get; set; } = "";
    public long Sequence { get; set; }
    public DateTimeOffset AcknowledgedAt { get; set; }
    public static string Key(string did, string topic) => Hash(did + "\n" + topic);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
