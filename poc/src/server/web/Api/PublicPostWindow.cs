namespace Tangent.Api;

public sealed record PublicParticipantDescription(string Ref, string Label, string Classification = "Undeclared",
    string? Avatar = null, string? ProfileUrl = null);

public sealed record PublicPostDescription(string Id, PublicParticipantDescription Author, string? Text,
    DateTimeOffset CreatedAt, DateTimeOffset AcceptedAt, DateTimeOffset? EditedAt, bool Removed, string Path,
    string Format = Conversation.PostContent.Plain, string? Html = null, string? SourceUri = null);

/// <summary>A stable, cache-safe page. Continuations name Posts rather than exposing storage positions.</summary>
public sealed record PublicPostWindow(PublicTopicDescription Topic, IReadOnlyList<PublicPostDescription> Posts,
    string? OlderBefore, string? NewerAfter, PublicPostDescription? OpeningPost = null);
