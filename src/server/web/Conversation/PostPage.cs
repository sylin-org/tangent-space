namespace TangentSpace.Conversation;

public sealed record PostPage(IReadOnlyList<Post> Messages, string? NextCursor, string ResumeCursor,
    long Boundary,
    IReadOnlyDictionary<string, string>? AuthorHandles = null,
    IReadOnlyDictionary<string, ParticipantResolution>? Resolved = null);
