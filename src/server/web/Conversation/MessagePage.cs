namespace TangentSpace.Conversation;

public sealed record MessagePage(IReadOnlyList<Message> Messages, string? NextCursor, string ResumeCursor,
    long Boundary, string Freshness, DateTimeOffset? LastCheckedAt,
    IReadOnlyDictionary<string, string>? AuthorHandles = null,
    IReadOnlyDictionary<string, ParticipantResolution>? Resolved = null);
