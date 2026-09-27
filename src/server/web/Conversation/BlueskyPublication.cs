using Koan.Data.Core.Model;

namespace Tangent.Conversation;

/// <summary>The durable link between one local Post and its remote record for one actor.</summary>
public sealed class BlueskyPublication : Entity<BlueskyPublication>
{
    public string ActorDid { get; set; } = "";
    public string LocalPostId { get; set; } = "";
    public string FirstRequestId { get; set; } = "";
    public string TextDigest { get; set; } = "";
    public string SourceUri { get; set; } = "";
    public string SourceCid { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; }
}
