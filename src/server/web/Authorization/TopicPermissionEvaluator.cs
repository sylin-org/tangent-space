using TangentSpace.Rooms;

namespace TangentSpace.Authorization;

/// <summary>
/// Pure typed permission evaluator over the current effective <see cref="RoomPolicy"/>, shared by HTTP and MCP
/// callers so per-action rules stop being duplicated. Fail closed: unknown capability values, a null policy and
/// synthetically inconsistent flags deny instead of allowing or throwing.
/// </summary>
/// <remarks>
/// Effective write, edit and delete always require <c>CanRead</c> as well as <c>CanWrite</c>, even when a supplied
/// policy flag combination could not arise from <c>Room.CurrentPolicy</c>. Management never requires <c>CanRead</c>,
/// matching the existing management-recovery behavior for not-yet-mapped spaces.
/// <para>
/// Denial reason precedence, most specific first:
/// <c>unsupported-action</c> (unknown value or capability not valid at this level),
/// <c>removed</c> (post already removed),
/// <c>not-author</c> (edit/delete require the post's author) or <c>use-delete-own-post</c> (own-post removal),
/// <c>manage-required</c>/<c>owner-required</c> (missing management or appointment authority),
/// the effective policy reason at the read/policy boundary (<c>policy.Reason</c> when it is a denial, else
/// <c>permission-denied</c>), then <c>topic-locked</c> and <c>editing-disabled</c> for otherwise admitted writers.
/// Every other denial, including synthetic flag inconsistencies, falls back to <c>permission-denied</c>.
/// </para>
/// </remarks>
public static class TopicPermissionEvaluator
{
    public const string AllowedReason = "allowed";
    public const string UnsupportedActionReason = "unsupported-action";
    public const string RemovedReason = "removed";
    public const string NotAuthorReason = "not-author";
    public const string OwnPostRemovalReason = "use-delete-own-post";
    public const string OwnPostReportReason = "own-post";
    public const string TopicLockedReason = "topic-locked";
    public const string EditingDisabledReason = "editing-disabled";
    public const string ManageRequiredReason = "manage-required";
    public const string OwnerRequiredReason = "owner-required";
    public const string PermissionDeniedReason = "permission-denied";

    /// <summary>Evaluates a Topic-level capability. EditOwnPost/DeleteOwnPost here are affordances only.</summary>
    public static TopicPermissionDecision Evaluate(RoomPolicy? policy, TopicCapability capability)
    {
        if (policy is null) return DenyNull(capability);
        if (!Enum.IsDefined(capability))
            return new TopicPermissionDecision(capability, false, UnsupportedActionReason, policy.SelectedPolicyRevision);
        return capability switch
        {
            TopicCapability.Read => ReadDecision(policy, capability),
            TopicCapability.Reply => WriteDecision(policy, capability, editingRequired: false),
            TopicCapability.ManageTopic or TopicCapability.ManageParticipants
                => new TopicPermissionDecision(capability, policy.CanManage, policy.CanManage ? AllowedReason : ManageRequiredReason, policy.SelectedPolicyRevision),
            TopicCapability.AppointManagers
                => new TopicPermissionDecision(capability, policy.CanAppointManagers, policy.CanAppointManagers ? AllowedReason : OwnerRequiredReason, policy.SelectedPolicyRevision),
            TopicCapability.EditOwnPost => WriteDecision(policy, capability, editingRequired: true),
            TopicCapability.DeleteOwnPost => WriteDecision(policy, capability, editingRequired: false),
            TopicCapability.RemovePost => RemoveDecision(policy, capability),
            TopicCapability.ReportPost => ReportDecision(policy, capability),
            _ => new TopicPermissionDecision(capability, false, UnsupportedActionReason, policy.SelectedPolicyRevision)
        };
    }

    /// <summary>Evaluates a Post-specific capability against its author and removal state.</summary>
    public static TopicPermissionDecision EvaluatePost(RoomPolicy? policy, TopicCapability capability, string? authorParticipantId, bool removed)
    {
        if (policy is null) return DenyNull(capability);
        if (!Enum.IsDefined(capability) || capability is not (TopicCapability.Read or TopicCapability.EditOwnPost
            or TopicCapability.DeleteOwnPost or TopicCapability.RemovePost or TopicCapability.ReportPost))
            return new TopicPermissionDecision(capability, false, UnsupportedActionReason, policy.SelectedPolicyRevision);
        var own = IsAuthor(policy.ActorParticipantId, authorParticipantId);
        if (capability is TopicCapability.EditOwnPost or TopicCapability.DeleteOwnPost)
        {
            if (removed) return Deny(policy, capability, RemovedReason);
            if (!own) return Deny(policy, capability, NotAuthorReason);
            return WriteDecision(policy, capability, editingRequired: capability == TopicCapability.EditOwnPost);
        }
        if (capability == TopicCapability.RemovePost)
        {
            if (removed) return Deny(policy, capability, RemovedReason);
            if (own) return Deny(policy, capability, OwnPostRemovalReason);
            return RemoveDecision(policy, capability);
        }
        if (capability == TopicCapability.ReportPost)
        {
            if (removed) return Deny(policy, capability, RemovedReason);
            if (string.IsNullOrWhiteSpace(policy.ActorParticipantId))
                return Deny(policy, capability, PermissionDeniedReason);
            if (own) return Deny(policy, capability, OwnPostReportReason);
            return ReadDecision(policy, capability);
        }
        return ReadDecision(policy, capability);
    }

    private static TopicPermissionDecision ReadDecision(RoomPolicy policy, TopicCapability capability)
        => policy.CanRead
            ? Allow(policy, capability)
            : Deny(policy, capability, PolicyBoundaryReason(policy));

    private static TopicPermissionDecision ReportDecision(RoomPolicy policy, TopicCapability capability)
        => string.IsNullOrWhiteSpace(policy.ActorParticipantId)
            ? Deny(policy, capability, PermissionDeniedReason)
            : ReadDecision(policy, capability);

    private static TopicPermissionDecision WriteDecision(RoomPolicy policy, TopicCapability capability, bool editingRequired)
    {
        if (!policy.CanRead) return Deny(policy, capability, PolicyBoundaryReason(policy));
        if (policy.Locked) return Deny(policy, capability, TopicLockedReason);
        if (editingRequired && !policy.EditingAllowed) return Deny(policy, capability, EditingDisabledReason);
        if (!policy.CanWrite) return Deny(policy, capability, PolicyBoundaryReason(policy));
        return Allow(policy, capability);
    }

    private static TopicPermissionDecision RemoveDecision(RoomPolicy policy, TopicCapability capability)
    {
        if (!policy.CanManage) return Deny(policy, capability, ManageRequiredReason);
        if (!policy.CanRead) return Deny(policy, capability, PolicyBoundaryReason(policy));
        return Allow(policy, capability);
    }

    private static bool IsAuthor(string? actorParticipantId, string? authorParticipantId) =>
        !string.IsNullOrWhiteSpace(actorParticipantId)
        && !string.IsNullOrWhiteSpace(authorParticipantId)
        && string.Equals(actorParticipantId, authorParticipantId, StringComparison.Ordinal);

    private static string PolicyBoundaryReason(RoomPolicy policy) =>
        !string.IsNullOrWhiteSpace(policy.Reason) && !string.Equals(policy.Reason, AllowedReason, StringComparison.Ordinal)
            ? policy.Reason
            : PermissionDeniedReason;

    private static TopicPermissionDecision Allow(RoomPolicy policy, TopicCapability capability)
        => new(capability, true, AllowedReason, policy.SelectedPolicyRevision);

    private static TopicPermissionDecision Deny(RoomPolicy policy, TopicCapability capability, string reason)
        => new(capability, false, reason, policy.SelectedPolicyRevision);

    private static TopicPermissionDecision DenyNull(TopicCapability capability)
        => new(capability, false, PermissionDeniedReason, 0);
}
