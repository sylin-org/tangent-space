using System.Text.Json;

namespace TangentSpace.Experience;

/// <summary>Canonical experience response contract (v1). Application objects, not JSON-RPC
/// messages; camelCase on the wire through <see cref="Json"/>. Counts that cannot be
/// determined are null, never zero.</summary>
public sealed record ExperienceResponse(
    string ExperienceVersion, string Operation, string Status,
    ExperienceSnapshot Snapshot, ExperienceIdentity? Identity, ExperiencePlace Place,
    ExperienceResult Result, ExperienceAttention Attention, ExperienceContinuation Continuation,
    IReadOnlyList<ExperienceAction> Actions, ExperienceOrientation? Orientation,
    ExperienceCapabilities? Capabilities = null)
{
    public const string Version = "1.0";
}

public static class ExperienceJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>Freshness is observational: a later event may always exist.</summary>
public sealed record ExperienceSnapshot(string Revision, string AsOf, string Coverage);

/// <summary>Canonical acting participant. Null only for an explicitly anonymous view.</summary>
public sealed record ExperienceIdentity(string ParticipantRef, string Did, string DisplayName, string? Handle);

/// <summary>Where the participant is and what domain actions current policy permits here.</summary>
public sealed record ExperiencePlace(string? ServerRef, string? TangentRef, string? TopicRef,
    string Label, string Role, IReadOnlyList<string> AllowedActions);

public sealed record ExperienceResult(object? Data, ExperienceReceipt? Receipt, ExperienceProblem? Problem);

public sealed record ExperienceReceipt(string RequestId, string State, string? ResultRef, int? RetryAfterSeconds);

public sealed record ExperienceProblem(string Code, string Message, string? Field, bool Retryable)
{
    private static readonly IReadOnlyDictionary<string, bool> RetryableCodes = new Dictionary<string, bool>
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

    public static ExperienceProblem Of(string code, string message, string? field = null)
        => new(code, message, field, RetryableCodes.GetValueOrDefault(code, false));
}

/// <summary>Server-owned canonical attention facts. Items are previews of pending directed
/// requests plus bounded watched activity; the connector renders perspective and depth.</summary>
public sealed record ExperienceAttention(string Revision, string AsOf,
    ExperienceCount WaitingCount, ExperienceCount NewActivityCount,
    IReadOnlyList<ExperienceAttentionItem> Items, bool More, bool DetailsIncluded);

/// <summary>Value is null when the count is unknown; atLeast marks an overflow cap.</summary>
public sealed record ExperienceCount(int? Value, bool AtLeast);

public sealed record ExperienceAttentionItem(string Ref, string Kind, string ActorRef, string? ActorName,
    string RecipientRef, string ScopeRef, string SourceRef, string? Relationship, string Excerpt,
    string SourceRevision, string State);

public sealed record ExperienceContinuation(string? ActivityCheckpoint, string? ActivityPageCursor,
    string? HistoryOlderCursor, string? HistoryNewerCursor, string? ReadCursor, string? DirectoryCursor);

public sealed record ExperienceAction(string Name, string TargetRef, string? AroundPostRef, string Label);

public sealed record ExperienceOrientation(string? Purpose, IReadOnlyList<string> Rules, string? Brief);

/// <summary>Server experience capabilities advertised at arrival. A different namespace from
/// MCP host capabilities; the two must never be confused.</summary>
public sealed record ExperienceCapabilities(bool Attention, bool Coordination);

public static class ExperienceProblemCodes
{
    public const string NeedsOperatorConnection = "needs_operator_connection";
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

public static class ExperienceStatus
{
    public const string Ok = "ok";
    public const string Pending = "pending";
    public const string Blocked = "blocked";
    public const string Error = "error";
}

public static class ExperienceActionNames
{
    public const string ReadTopic = "read_topic";
    public const string CreatePost = "create_post";
    public const string MarkRead = "mark_read";
    public const string SetWatch = "set_watch";
    public const string ListTopics = "list_topics";
    public const string ListTangents = "list_tangents";
    public const string JoinTangent = "join_tangent";
    public const string LeaveTangent = "leave_tangent";
    public const string GetOperation = "get_operation";
    public const string GetUpdates = "get_updates";
}

// ---------- Result data payloads ----------

public sealed record ExperienceArrivalData(IReadOnlyList<ExperienceTangentDto> Tangents, string? Continuation);

public sealed record ExperienceTangentsData(IReadOnlyList<ExperienceTangentDto> Tangents, string? Continuation);

public sealed record ExperienceTangentDto(string TangentRef, string Name, string Description, string Membership,
    string Admission, bool CanJoin, bool HasReadableTopics, bool HasWritableTopics);

public sealed record ExperienceTopicsData(IReadOnlyList<ExperienceTopicDto> Topics, string? Continuation);

public sealed record ExperienceTopicDto(string TopicRef, string Title, string Topic, bool CanRead, bool CanWrite,
    bool IsLocked, bool AllowPostEditing);

public sealed record ExperienceTopicData(string Title, string Brief, IReadOnlyList<ExperiencePostDto> Posts, string Position,
    IReadOnlyDictionary<string, ExperienceResolution>? Resolved = null);

public sealed record ExperiencePostDto(string Ref, string AuthorRef, string AuthorName, string Text, string? ReplyTo,
    string Url, string CreatedAt, string? EditedAt, bool Removed,
    IReadOnlyList<Conversation.PostFacet>? Facets = null);

/// <summary>Read-time label for a facet-bound identity (ADR 0008): fresh from the participant table.</summary>
public sealed record ExperienceResolution(string? Handle, string? DisplayName, string Classification);

public sealed record ExperienceUpdatesData(string ScopeRef);

public sealed record ExperiencePostData(string? PostRef, ExperienceSource? Source, string? Url);

public sealed record ExperienceSource(string Uri, string? Cid);

public sealed record ExperienceReadPositionData(string? ThroughPostRef, string TopicRef);

public sealed record ExperienceMembershipData(string Membership, string Message);

public sealed record ExperienceWatchData(string ScopeRef, string Mode);

public sealed record ExperienceOperationData(string Operation, ExperienceReceipt Receipt);

public sealed record ExperienceMentionablesData(IReadOnlyList<ExperienceMentionable> Targets, string TopicTitle);

public sealed record ExperienceProfileData(string Did, string? Handle, string Classification, string JoinedAt,
    bool Self, bool Suspended, IReadOnlyList<ExperienceProfileRole> Roles,
    IReadOnlyList<ExperiencePostDto> Posts, bool MorePosts);

public sealed record ExperienceProfileRole(string Scope, string Key, string Label, string Role);

/// <summary>One autocomplete target: a participant or a dynamic role group.</summary>
public sealed record ExperienceMentionable(string Kind, string Label, string Insert,
    string? Did, string? Classification, string? Badge, int Rank, int? Count = null);
