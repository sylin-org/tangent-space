namespace TangentSpace.Conversation;

public sealed record ConversationCursor(string ParticipantId, string Topic, long After, long? Boundary, DateTimeOffset ExpiresAt);
