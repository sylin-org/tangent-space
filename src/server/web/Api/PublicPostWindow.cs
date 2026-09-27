namespace Tangent.Api;

public sealed record PublicParticipantDescription(string Ref, string Label);

public sealed record PublicPostDescription(string Id, PublicParticipantDescription Author, string? Text,
    DateTimeOffset CreatedAt, DateTimeOffset AcceptedAt, DateTimeOffset? EditedAt, bool Removed, string Path);

/// <summary>A stable, cache-safe page. Continuations name Posts rather than exposing storage positions.</summary>
public sealed record PublicPostWindow(PublicTopicDescription Topic, IReadOnlyList<PublicPostDescription> Posts,
    string? OlderBefore, string? NewerAfter);
