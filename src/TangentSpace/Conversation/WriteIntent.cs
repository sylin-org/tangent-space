using Koan.Data.Core.Model;

namespace TangentSpace.Conversation;

public sealed class WriteIntent : Entity<WriteIntent>
{
    public string RoomKey { get; set; } = "";
    public string AuthorDid { get; set; } = "";
    public string OperationId { get; set; } = "";
    public string RecordKey { get; set; } = "";
    public string SpaceUri { get; set; } = "";
    public MessageContent Content { get; set; } = new("", default, null);
    public string State { get; set; } = "pending";
    public string? SourceUri { get; set; }
    public string? SourceCid { get; set; }
    public string? Detail { get; set; }
    public static string Key(string did, string room, string operationId) => SourceDecision.Hash(did + "\n" + room + "\n" + operationId);
}
