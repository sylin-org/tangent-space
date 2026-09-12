namespace TangentSpace.Conversation;

/// <summary>A retained history window; activity snapshots and read acknowledgement are separate.</summary>
public sealed record McpMessageWindow(IReadOnlyList<Message> Messages, string? OlderCursor, string? NewerCursor,
    string? ReadCursor, string Position, string Freshness, DateTimeOffset? LastCheckedAt,
    IReadOnlyDictionary<string, string> AuthorHandles,
    IReadOnlyDictionary<string, ParticipantResolution>? Resolved = null);

internal sealed record McpHistoryCursor(string ParticipantId, string Room, long Boundary, long Edge, bool Older,
    DateTimeOffset ExpiresAt);
