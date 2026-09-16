using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace TangentSpace.Rooms;

public sealed class TopicMembership : Entity<TopicMembership>
{
    public string RoomKey { get; set; } = "";
    public string ParticipantId { get; set; } = "";
    public TopicRole Role { get; set; }
    public string ChangedByParticipantId { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; }
    public long PolicyRevision { get; set; }

    public static string Key(string roomKey, string participantDid)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(roomKey + "\n" + participantDid)));

    internal static TopicMembership Assign(Topic topic, string participantDid, TopicRole role, string actorDid, DateTimeOffset now)
        => new()
        {
            Id = Key(topic.Id, participantDid), RoomKey = topic.Id, ParticipantId = participantDid, Role = role,
            ChangedByParticipantId = actorDid, ChangedAt = now, PolicyRevision = topic.PolicyRevision
        };
}
