using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace TangentSpace.Activity;

/// <summary>Per participant and Tangent default; a channel's explicit WatchSetting overrides it. Never affects access.</summary>
public sealed class TangentWatchSetting : Entity<TangentWatchSetting>
{
    public string ParticipantDid { get; set; } = "";
    public string TangentKey { get; set; } = "";
    public WatchMode Mode { get; set; }
    public string ChangedByDid { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; }

    // Namespaced so a Tangent default can never collide with a same-named channel's watch setting.
    public static string Key(string participantDid, string tangentKey)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("tangent-watch\n" + participantDid + "\n" + tangentKey)));

    public static TangentWatchSetting Choose(string participantDid, string tangentKey, WatchMode mode, string actorDid, DateTimeOffset now)
    {
        if (!Enum.IsDefined(mode)) throw new InvalidOperationException("Choose all, replies, or none.");
        return new TangentWatchSetting
        {
            Id = Key(participantDid, tangentKey), ParticipantDid = participantDid, TangentKey = tangentKey,
            Mode = mode, ChangedByDid = actorDid, ChangedAt = now
        };
    }
}
