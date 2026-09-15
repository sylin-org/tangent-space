namespace TangentSpace.Conversation;

/// <summary>A bounded window of one Topic's history. Activity snapshots and read acknowledgement are separate.</summary>
public sealed record TopicWindow(IReadOnlyList<Message> Messages, string? OlderCursor, string? NewerCursor,
    string? ReadCursor, string Position,
    IReadOnlyDictionary<string, string> AuthorHandles,
    IReadOnlyDictionary<string, ParticipantResolution>? Resolved = null);

/// <summary>A protected continuation of one participant's window over one Topic.</summary>
internal sealed record WindowCursor(string ParticipantId, string TopicKey, long Boundary, long Edge, bool Older,
    DateTimeOffset ExpiresAt);
