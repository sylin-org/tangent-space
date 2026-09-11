using Koan.Data.Core.Model;

namespace TangentSpace.Conversation;

public sealed class PostChange : Entity<PostChange>
{
    public string RoomKey { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string ActorDid { get; set; } = "";
    public string OperationId { get; set; } = "";
    public string State { get; set; } = "pending";
    public string Detail { get; set; } = "";
    public bool Delete { get; set; }
    public string? Text { get; set; }
    /// <summary>The client-sent facet payload of the delivery (null when server re-detection was
    /// used). The idempotency conflict check compares this ledger copy, never the live row.</summary>
    public IReadOnlyList<PostFacet>? Facets { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public static string Key(string did, string room, string message, string operation)
        => SourceDecision.Hash($"post-change\n{did}\n{room}\n{message}\n{operation}");
}

public sealed record PostChangeResult(string State, string MessageId, string Detail);
