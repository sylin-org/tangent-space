using Koan.Data.Core.Model;

namespace TangentSpace.Conversation;

public sealed class Message : Entity<Message>
{
    public string RoomKey { get; set; } = "";
    public string AuthorDid { get; set; } = "";
    public string SourceUri { get; set; } = "";
    public string SourceCid { get; set; } = "";
    public long Sequence { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public MessageContent Content { get; set; } = new("", default, null);
    public static Message Project(SourceDecision source) => new()
    {
        Id = source.Id, RoomKey = source.RoomKey, AuthorDid = source.AuthorDid, SourceUri = source.SourceUri,
        SourceCid = source.SourceCid, Sequence = source.Sequence, AcceptedAt = source.DecidedAt, Content = source.Content!
    };
}
