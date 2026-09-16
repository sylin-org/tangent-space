using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace TangentSpace.Communities;

public sealed class TangentMembership : Entity<TangentMembership>
{
    public string TangentKey { get; set; } = "";
    public string ParticipantId { get; set; } = "";
    public TangentRole Role { get; set; }
    public string ChangedByParticipantId { get; set; } = "";
    public DateTimeOffset ChangedAt { get; set; }

    public static string Key(string tangentKey, string participantDid)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tangentKey + "\n" + participantDid)));

    internal static TangentMembership Assign(Tangent tangent, string did, TangentRole role, string actorDid, DateTimeOffset now) => new()
    {
        Id = Key(tangent.Id, did), TangentKey = tangent.Id, ParticipantId = did, Role = role, ChangedByParticipantId = actorDid, ChangedAt = now
    };
}
