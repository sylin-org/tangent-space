using Koan.Data.Core.Model;
using Koan.Data.Abstractions.Annotations;

namespace Tangent.Media;

/// <summary>An immutable upload belongs to its author until one Post adopts it.</summary>
[Index(Fields = [nameof(OwnerParticipantId), nameof(PostId), nameof(Id)])]
public sealed class PostMediaAsset : Entity<PostMediaAsset>
{
    public string StoreId { get; set; } = "";
    public string OwnerParticipantId { get; set; } = "";
    public string TangentKey { get; set; } = "";
    public string? TopicKey { get; set; }
    public string? PostId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public string Extension { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
