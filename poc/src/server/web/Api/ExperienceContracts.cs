namespace Tangent.Api;

/// <summary>A resource read or write and its policy outcome. The HTTP layer maps the
/// outcome to a status code; each successful endpoint has its own concrete DTO.</summary>
public sealed record ResourceResult<T>(T? Data, ExperienceProblem? Problem = null, bool Pending = false)
{
    public static ResourceResult<T> Success(T data) => new(data);
    public static ResourceResult<T> Accepted(T data) => new(data, Pending: true);
    public static ResourceResult<T> Failure(ExperienceProblem problem) => new(default, problem);
}

/// <summary>Freshness is observational: a later event may always exist.</summary>

/// <summary>Canonical acting participant, keyed by the GUID participant reference. Null only for
/// an explicitly anonymous view; Did is present only while an atproto identity is held.</summary>
public sealed record ExperienceIdentity(string ParticipantRef, string? Did, string DisplayName, string? Handle);

public sealed record ExperiencePlace(string ServerRef, string Label, string Role);

/// <summary>Transport grants, current Topic stewardship when scopeRef names a Topic,
/// and independent global participant-management facts. Every action checks current
/// resource policy when it runs.</summary>
public sealed record ExperienceAuthority(bool CanRead, bool CanPost, bool CanManage, bool CanStewardScope,
    ExperienceManagementAuthority Management);

public sealed record ExperienceManagementAuthority(bool CanWarn, bool CanTimeout,
    bool CanSuspend, bool CanBan, bool CanAssignRoles)
{
    public bool HasAny() => CanWarn || CanTimeout || CanSuspend || CanBan || CanAssignRoles;
}


public sealed record ExperienceProblem(string Code, string Message, string? Field, bool Retryable)
{
    private static readonly IReadOnlyDictionary<string, bool> RetryableCodes = new Dictionary<string, bool>
    {
        ["context_expired"] = false,
        ["not_admitted"] = false,
        ["permission_denied"] = false,
        ["cursor_expired"] = false,
        ["request_conflict"] = false,
        ["unreachable"] = true,
        ["invalid_arguments"] = false
    };

    public static ExperienceProblem Of(string code, string message, string? field = null)
        => new(code, message, field, RetryableCodes.GetValueOrDefault(code, false));
}

/// <summary>Server-owned canonical attention facts. Items are previews of pending directed
/// requests plus bounded watched activity; the connector renders perspective and depth.</summary>
public sealed record ExperienceAttention(string Revision, string AsOf,
    ExperienceCount WaitingCount, ExperienceCount NewActivityCount,
    IReadOnlyList<ExperienceAttentionItem> Items, bool More, bool DetailsIncluded, bool Incomplete = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Selection { get; init; }
}

/// <summary>Value is null when the count is unknown; atLeast marks an overflow cap or incomplete coverage.</summary>
public sealed record ExperienceCount(int? Value, bool AtLeast);

public sealed record ExperienceAttentionItem(string Ref, string Kind, string ActorRef, string? ActorName,
    string RecipientRef, string ScopeRef, string SourceRef, string? Relationship, string Excerpt,
    string SourceRevision, string State);



public static class ExperienceProblemCodes
{
    public const string NotAdmitted = "not_admitted";
    public const string PermissionDenied = "permission_denied";
    public const string CursorExpired = "cursor_expired";
    public const string RequestConflict = "request_conflict";
    public const string Unreachable = "unreachable";
    public const string InvalidArguments = "invalid_arguments";
    public const string ModerationConflict = "moderation_conflict";
}

// ---------- Result data payloads ----------

public sealed record ExperienceArrivalData(IReadOnlyList<ExperienceTangentDto> Tangents, string? Continuation,
    ExperienceIdentity Identity, ExperiencePlace Place, ExperienceAuthority Authority, ExperienceAttention Attention);

public sealed record ExperienceTangentsData(IReadOnlyList<ExperienceTangentDto> Tangents, string? Continuation);

public sealed record ExperienceTangentDto(string TangentRef, string Name, string Description, string Membership,
    string Admission, bool CanJoin, bool HasReadableTopics, bool HasWritableTopics,
    string Kind = Community.TangentKind.Discussions);

public sealed record ExperienceTopicsData(IReadOnlyList<ExperienceTopicDto> Topics, string? Continuation);

public sealed record ExperienceTopicDto(string TopicRef, string Title, string Topic, bool CanRead, bool CanWrite,
    bool IsLocked, bool AllowPostEditing, string Presentation = Community.TopicPresentation.Conversation);

public sealed record ExperienceTopicData(string Title, string Brief, IReadOnlyList<ExperiencePostDto> Posts, string Position,
    IReadOnlyDictionary<string, ExperienceResolution>? Resolved = null,
    bool IsLocked = false, bool AllowPostEditing = true,
    string? HistoryOlderCursor = null, string? HistoryNewerCursor = null, string? ReadCursor = null,
    string? ServerRef = null, string? TangentRef = null, string? TopicRef = null,
    bool CanWrite = false, bool Stewardship = false,
    string Presentation = Community.TopicPresentation.Conversation, ExperiencePostDto? OpeningPost = null);

public sealed record ExperiencePostDto(string Ref, string AuthorRef, string AuthorName, string Text, string? ReplyTo,
    string Url, string CreatedAt, string? EditedAt, bool Removed,
    IReadOnlyList<Conversation.PostFacet>? Facets = null, ExperiencePostSource? Source = null,
    string Format = Conversation.PostContent.Plain, string? Html = null);

public sealed record ExperiencePostSource(string Service, string Uri, string RecordId);

/// <summary>Read-time label for a facet-bound identity (ADR 0008): fresh from the participant table.</summary>
public sealed record ExperienceResolution(string? Handle, string? DisplayName, string Classification,
    string? Avatar = null, string? ProfileUrl = null);

public sealed record ExperienceUpdatesData(string ScopeRef, ExperienceAttention Attention,
    string ActivityCheckpoint, string? ActivityPageCursor);

public sealed record ExperiencePostData(string? PostRef, ExperienceSource? Source, string? Url,
    string? RequestId = null);

/// <summary>A reporter-safe acknowledgement. Moderation case identity, state, testimony totals,
/// steward decisions and escalation ownership are deliberately not disclosed to reporters.</summary>
public sealed record ExperienceReportData(string PostRef, bool Accepted, bool AlreadyReported, bool CaseSaturated);

public sealed record ExperienceSource(string Uri, string? Cid);

public sealed record ExperienceReadPositionData(string? ThroughPostRef, string TopicRef);

public sealed record ExperienceMembershipData(string Membership, string Message);

public sealed record ExperienceWatchData(string ScopeRef, string Mode);

public sealed record ExperienceMentionablesData(IReadOnlyList<ExperienceMentionable> Targets, string TopicTitle);

public sealed record ExperienceProfileData(string ParticipantRef, string? Did, string? Handle, string Classification,
    string JoinedAt, bool Self, bool Suspended, IReadOnlyList<ExperienceProfileRole> Roles,
    IReadOnlyList<ExperiencePostDto> Posts, bool MorePosts,
    string? DisplayName = null, string? Description = null, string? Avatar = null, string? ProfileUrl = null,
    IReadOnlyDictionary<string, Conversation.ParticipantResolution>? Resolved = null,
    IReadOnlyList<ExperienceProfileManagementLink>? RoleManagement = null);

public sealed record ExperienceProfileManagementLink(string TangentKey, string Label);

public sealed record ExperienceProfileRole(string Scope, string Key, string Label, string Role);

/// <summary>One autocomplete target: a participant or a dynamic role group.</summary>
public sealed record ExperienceMentionable(string Kind, string Label, string Insert,
    string? Did, string? Classification, string? Badge, int Rank, int? Count = null);

/// <summary>One rung of the user-management ladder, applied. Until is the timeout's computed
/// expiry; TangentRef scopes a restriction to the Tangent its cited case lives in.</summary>
public sealed record ExperienceUserManagementData(string UserRef, string Action, string Reason,
    string? Role = null, DateTimeOffset? Until = null, string? TangentRef = null, string? CaseRef = null);

/// <summary>A thread started through one job: the Topic and its opening Post.</summary>
public sealed record ExperienceThreadData(string TopicRef, string TangentRef, string PostRef, string Title, string Url,
    string? RequestId = null);

/// <summary>The participant read for a companion: an identity card and a bounded, read-filtered
/// window of their recent Posts — the contextualization instrument.</summary>
public sealed record ExperienceReadUserData(ExperienceReadUserProfile Profile,
    IReadOnlyList<ExperiencePostDto> RecentPosts, bool MorePosts,
    IReadOnlyDictionary<string, Conversation.ParticipantResolution>? Resolved = null);

public sealed record ExperienceReadUserProfile(string ParticipantRef, string? Did, string? Handle,
    string DisplayName, string Classification, string? Description, string? Avatar, string ProfileUrl);
