namespace Tangent.Conversation;

/// <summary>A bounded content read from one configured source. Source-specific identifiers
/// stay attached to each item so local and remote history never masquerade as one another.</summary>
public interface IContentSource
{
    string Key { get; }
    Task<ContentPage?> Read(ContentQuery query, CancellationToken ct);
}

public sealed record ContentQuery(string? TangentKey, string? TopicKey, string? ActorDid,
    string? Before, string? After, int Limit);

public sealed record ContentItem(string Source, string Id, string Author, string Text,
    DateTimeOffset CreatedAt, string Url, string? SourceUri = null, string? SourceCid = null);

public sealed record ContentPage(string Source, IReadOnlyList<ContentItem> Posts,
    string? OlderBefore, string? NewerAfter);
