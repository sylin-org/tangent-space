using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace TangentSpace.Communities;

/// <summary>A durable admission request under manual approval. Pending is not membership and grants nothing.</summary>
public sealed class TangentJoinRequest : Entity<TangentJoinRequest>
{
    public string TangentKey { get; set; } = "";
    public string ParticipantDid { get; set; } = "";
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public bool? Accepted { get; set; }
    public string DecidedByDid { get; set; } = "";

    public bool Pending => DecidedAt is null;

    public static string Key(string tangentKey, string participantDid)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tangentKey + "\n" + participantDid)));

    public static TangentJoinRequest Open(string tangentKey, string participantDid, DateTimeOffset now) => new()
    {
        Id = Key(tangentKey, participantDid), TangentKey = tangentKey, ParticipantDid = participantDid, RequestedAt = now
    };

    public void Decide(string actorDid, bool accepted, DateTimeOffset now)
    {
        if (!Pending) throw new TangentRuleViolation(TangentDenial.InvalidInput, "This admission request was already decided.");
        DecidedAt = now;
        Accepted = accepted;
        DecidedByDid = actorDid;
    }
}
