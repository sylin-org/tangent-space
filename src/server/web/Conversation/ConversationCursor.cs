namespace TangentSpace.Conversation;

public sealed record ConversationCursor(string Did, string Room, long After, long? Boundary, DateTimeOffset ExpiresAt);
