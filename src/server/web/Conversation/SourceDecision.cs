using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace TangentSpace.Conversation;

/// <summary>Durable first acceptance decision and retained source content; the projection can be rebuilt from this ledger.</summary>
public sealed class SourceDecision : Entity<SourceDecision>
{
    public string RoomKey { get; set; } = "";
    public string AuthorParticipantId { get; set; } = "";
    public string SourceUri { get; set; } = "";
    public string SourceCid { get; set; } = "";
    public bool Accepted { get; set; }
    public string Reason { get; set; } = "";
    public long PolicyRevision { get; set; }
    public long SpacePolicyRevision { get; set; }
    public DateTimeOffset DecidedAt { get; set; }
    public long Sequence { get; set; }
    public PostContent? Content { get; set; }
    public static string Key(string topic, string uri, string cid) => Hash(topic + "\n" + uri + "\n" + cid);
    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
