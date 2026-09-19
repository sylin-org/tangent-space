namespace Tangent.Conversation;

/// <summary>An operation id was reused with different content: the conflict is the action's own
/// answer, and the adapter renders it (the experience API answers 409 request_conflict).</summary>
public sealed class OperationConflictException(string operationId) : Exception($"Operation {operationId} was already used with different content.");
