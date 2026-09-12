namespace TangentSpace.Conversation;

public sealed record ConversationCursor(string ParticipantId, string Room, long After, long? Boundary, DateTimeOffset ExpiresAt);
