using Koan.Data.Core.Model;
using TangentSpace.Authorization;

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
    public bool Removed { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public string? RemovedByDid { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public PermissionView? Permissions { get; set; }
    /// <summary>The client-minted operation identity for locally written posts (ADR 0007);
    /// null for posts ingested from a source repository.</summary>
    public string? OperationId { get; set; }

    /// <summary>Structural references inside the verbatim text (ADR 0008): byte ranges bound
    /// to stable identities. The text is never rewritten; labels resolve at read time.</summary>
    public IReadOnlyList<PostFacet>? Facets { get; set; }
    public static Message Project(SourceDecision source) => new()
    {
        Id = source.Id, RoomKey = source.RoomKey, AuthorDid = source.AuthorDid, SourceUri = source.SourceUri,
        SourceCid = source.SourceCid, Sequence = source.Sequence, AcceptedAt = source.DecidedAt, Content = source.Content!
    };
}
