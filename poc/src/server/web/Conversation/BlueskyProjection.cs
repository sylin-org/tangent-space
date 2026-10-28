using Koan.Data.Core.Model;

namespace Tangent.Conversation;

/// <summary>A durable local projection of a public Bluesky Post. Source identity survives
/// a handle change and is never inferred from the local Post id.</summary>
public sealed class BlueskyProjection : Entity<BlueskyProjection>
{
    public string ActorDid { get; set; } = "";
    public string AuthorHandle { get; set; } = "";
    public string SourceUri { get; set; } = "";
    public string SourceCid { get; set; } = "";
    public string Text { get; set; } = "";
    public string? ReplyToUri { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public long Sequence { get; set; }
    public bool SeenInFeed { get; set; }
    public bool Hidden { get; set; }
}

/// <summary>The selected public author and import position. The source cursor is held here,
/// while callers page the durable projection by Post identity.</summary>
public sealed class BlueskySourceState : Entity<BlueskySourceState>
{
    public string Handle { get; set; } = "";
    public DateTimeOffset SweepStartedAt { get; set; }
    public string? SweepCursor { get; set; }
    public bool ReconcilePending { get; set; }
    public DateTimeOffset RefreshedAt { get; set; }
}
