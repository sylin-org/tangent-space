namespace Tangent.Api;

public sealed record PublicParticipantDescription(string Ref, string Label);

public sealed record PublicPostDescription(string Id, PublicParticipantDescription Author, string? Text,
    DateTimeOffset CreatedAt, DateTimeOffset AcceptedAt, DateTimeOffset? EditedAt, bool Removed, string Path);

/// <summary>A stable, cache-safe page. Sequence edges are data positions rather than expiring participant cursors.</summary>
public sealed record PublicPostWindow(PublicTopicDescription Topic, IReadOnlyList<PublicPostDescription> Posts,
    long? OlderBefore, long? NewerAfter);
