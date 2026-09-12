using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace TangentSpace.Rooms;

public sealed class RoomMembership : Entity<RoomMembership>
{
    public string RoomKey { get; set; } = "";
    public string ParticipantId { get; set; } = "";
    public RoomRole Role { get; set; }
    public string ChangedByParticipantId { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; }
    public long PolicyRevision { get; set; }

    public static string Key(string roomKey, string participantDid)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(roomKey + "\n" + participantDid)));

    internal static RoomMembership Assign(Room room, string participantDid, RoomRole role, string actorDid, DateTimeOffset now)
        => new()
        {
            Id = Key(room.Id, participantDid), RoomKey = room.Id, ParticipantId = participantDid, Role = role,
            ChangedByParticipantId = actorDid, ChangedAt = now, PolicyRevision = room.PolicyRevision
        };
}
