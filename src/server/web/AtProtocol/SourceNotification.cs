using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace TangentSpace.AtProtocol;

/// <summary>Durable, coalesced dirty-repository hint. Its content is never a conversation record.</summary>
public sealed class SourceNotification : Entity<SourceNotification>
{
    public string RoomKey { get; set; } = "";
    public string Space { get; set; } = "";
    public string AuthorDid { get; set; } = "";
    public string Revision { get; set; } = "";
    public string CommitHash { get; set; } = "";
    public long Generation { get; set; }
    public bool Pending { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string Status { get; set; } = "pending";

    public static string Key(string room, string author)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(room + "\n" + author)));
}
