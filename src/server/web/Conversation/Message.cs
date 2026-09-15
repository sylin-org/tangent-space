using Koan.Data.Core.Model;
using TangentSpace.Authorization;

namespace TangentSpace.Conversation;

public sealed class Message : Entity<Message>
{
    public string RoomKey { get; set; } = "";
    public string AuthorParticipantId { get; set; } = "";
    public string SourceUri { get; set; } = "";
    public string SourceCid { get; set; } = "";
    public long Sequence { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
    public MessageContent Content { get; set; } = new("", default, null);
    public bool Removed { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
    public string? RemovedByParticipantId { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public PermissionView? Permissions { get; set; }
    /// <summary>The client-minted operation identity for locally written posts (ADR 0007);
    /// null for posts ingested from a source repository.</summary>
    public string? OperationId { get; set; }

    /// <summary>Structural references inside the verbatim text (ADR 0008): byte ranges bound
    /// to stable identities. Null asks the Message save hook to derive a package; an empty
    /// package deliberately opts out. Snapshots and removals are never reinterpreted.
    /// The text is never rewritten; labels resolve at read time.</summary>
    public IReadOnlyList<PostFacet>? Facets { get; set; }

    /// <summary>The changelog partition name: pre-edit snapshots are insert-only rows
    /// materialized as <c>TangentSpace.Conversation.Message#changelog</c> (adapter separator '#').</summary>
    public const string ChangelogPartition = "changelog";

    /// <summary>Snapshot rows only: the live row this pre-edit copy archives. Live rows carry null.</summary>
    public string? OfMessageId { get; set; }

    /// <summary>Snapshot rows only: the live row's ChangeId when this snapshot was minted, so
    /// snapshots chain oldest → newest. Null on the original's first snapshot; live rows carry null.</summary>
    public string? PreviousChangeId { get; set; }

    /// <summary>Live rows only: the newest changelog snapshot for this message. Null until the first change.</summary>
    public string? ChangeId { get; set; }

    public static Message Project(SourceDecision source) => new()
    {
        Id = source.Id, RoomKey = source.RoomKey, AuthorParticipantId = source.AuthorParticipantId, SourceUri = source.SourceUri,
        SourceCid = source.SourceCid, Sequence = source.Sequence, AcceptedAt = source.DecidedAt, Content = source.Content!
    };
}
