using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace Tangent.Activity;

public sealed record WatchResult(string RoomKey, WatchMode Mode);

/// <summary>Per participant and channel; absent means All. Watch state and read state are independent.</summary>
public sealed class WatchSetting : Entity<WatchSetting>
{
    public string ParticipantId { get; set; } = "";
    public string RoomKey { get; set; } = "";
    public WatchMode Mode { get; set; }
    public string ChangedByParticipantId { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; }

    // Namespaced so channel watch state can never collide with read positions or Tangent-wide defaults,
    // even when a topic and a Tangent share the same key.
    public static string Key(string participantDid, string roomKey)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("watch\n" + participantDid + "\n" + roomKey)));

    public static WatchSetting Choose(string participantDid, string roomKey, WatchMode mode, string actorDid, DateTimeOffset now)
    {
        if (!Enum.IsDefined(mode)) throw new InvalidOperationException("Choose all, replies, or none.");
        return new WatchSetting
        {
            Id = Key(participantDid, roomKey), ParticipantId = participantDid, RoomKey = roomKey,
            Mode = mode, ChangedByParticipantId = actorDid, ChangedAt = now
        };
    }
}
