namespace TangentSpace.Mcp;

/// <summary>The fixed five-segment application envelope from the Tangent MCP contract.</summary>
public sealed record McpEnvelope(string Operation, string Status, string? CompanionId, string? ContextId,
    McpIdentity? Identity, McpPlace Place, McpResult Result, McpActivity Activity, McpNext Next)
{
    public const string ContractVersion = McpContractCatalog.ContractVersion;
}

public sealed record McpIdentity(string Did, string ActingAs, string DisplayName, string ExpiresAt);

public sealed record McpPlace(string Kind, string Label, string? ServerRef, string? TangentRef, string? ChannelRef,
    IReadOnlyList<string> Permissions, string Readiness, TangentSpace.Authorization.PermissionView? Access = null);

public sealed record McpResult(object? Data, McpReceipt? Receipt, McpProblem? Problem);

public sealed record McpReceipt(string RequestId, string OperationRef, string State, string? ResultRef, int? RetryAfterSeconds);

public sealed record McpProblem(string Code, string Message, string? Field, bool Retryable)
{
    public static readonly IReadOnlyDictionary<string, bool> RetryableCodes = new Dictionary<string, bool>
    {
        ["companion_unavailable"] = false,
        ["context_expired"] = false,
        ["needs_operator_connection"] = true,
        ["not_admitted"] = false,
        ["approval_pending"] = true,
        ["source_permission_missing"] = true,
        ["source_unsupported"] = false,
        ["permission_denied"] = false,
        ["cursor_expired"] = false,
        ["request_conflict"] = false,
        ["unreachable"] = true,
        ["invalid_arguments"] = false,
        ["unsupported_operation"] = false,
        ["receipt_expired"] = false
    };

    public static McpProblem Of(string code, string message, string? field = null)
        => new(code, message, field, RetryableCodes.GetValueOrDefault(code, false));
}

public sealed record McpActivity(string AsOf, string Coverage, IReadOnlyList<McpNotice> Notices, bool More);

public sealed record McpNotice(string ChannelRef, string Label, McpCount Unread, McpCount RepliesToYou,
    McpCount Mentions, string Revision);

public sealed record McpCount(int Value, bool AtLeast);

public sealed record McpNext(IReadOnlyList<string> Available, IReadOnlyList<McpNextCall> Calls);

public sealed record McpNextCall(string Tool, string Label, string ArgumentsJson);

public static class McpProblemCodes
{
    public const string CompanionUnavailable = "companion_unavailable";
    public const string ContextExpired = "context_expired";
    public const string NotAdmitted = "not_admitted";
    public const string ApprovalPending = "approval_pending";
    public const string SourcePermissionMissing = "source_permission_missing";
    public const string SourceUnsupported = "source_unsupported";
    public const string PermissionDenied = "permission_denied";
    public const string CursorExpired = "cursor_expired";
    public const string RequestConflict = "request_conflict";
    public const string Unreachable = "unreachable";
    public const string InvalidArguments = "invalid_arguments";
    public const string UnsupportedOperation = "unsupported_operation";
    public const string ReceiptExpired = "receipt_expired";
}
