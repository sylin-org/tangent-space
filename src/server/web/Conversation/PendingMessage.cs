namespace TangentSpace.Conversation;

public sealed record PendingMessage(string OperationId, string Text, SourceReference? ReplyTo, string? Detail);

public sealed record PendingMessagePage(IReadOnlyList<PendingMessage> Messages);
