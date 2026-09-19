using Tangent.Community;
using Tangent.Identity;
using Tangent.Stewardship;

namespace Tangent.Access;

/// <summary>
/// One evaluator answers every Topic question (ADR 0013). Resolution: a null decision inherits its
/// parent's; a listed decision intersects with it, so a child narrows and never widens; an empty
/// See means everyone, and empty Post, Manage or Create Topics means the category's always-on
/// permission holders alone — that table lives in <see cref="AccessMap"/>'s defaults and
/// resolutions. The actor's role bag and its derived tokens grant; restrictions, suspension and
/// classification bound what any grant reaches; ownership is derived from the Space, the Tangent
/// or topic authorship, never read from a record. The transitional membership arithmetic
/// (<c>Topic.CurrentPolicy</c>) feeds selected facts into <see cref="Decide"/> but decides nothing.
/// </summary>
public static class AccessEvaluator
{
    /// <summary>The final decision for one actor on one Topic. The bag arrives as its tokens —
    /// Koan's compiled answer — because the question is a plain any-of match.</summary>
    public static TopicPolicy Decide(Topic topic, Community.Tangent? tangent, TopicPolicy selected,
        IReadOnlyCollection<string> bagTokens,
        bool suspended, EffectiveRestriction? restriction, ParticipantClassification classification)
    {
        var effective = (topic.Access ?? AccessMap.TopicDefaults()).ResolveTopic(tangent?.Access ?? AccessMap.TangentDefaults());
        var ownsResource = selected.IsOwner
            || string.Equals(topic.CreatorParticipantId, selected.ActorParticipantId, StringComparison.Ordinal);
        var siteUnavailable = selected.SpacePolicyRevision <= 0;
        var missingParent = tangent is null;
        var banned = restriction is { Banned: true } && !ownsResource;
        var timedOut = restriction is { Banned: false } && !ownsResource;
        var rights = tangent?.ParticipationRights(classification) ?? (Read: false, Write: false);
        var read = !siteUnavailable && !suspended && !missingParent && !banned && rights.Read
            && (ownsResource || CanDo(effective.See, bagTokens));
        var write = read && !timedOut && !topic.IsLocked && rights.Write
            && (ownsResource || CanDo(effective.Post, bagTokens));
        var manage = !siteUnavailable && !suspended && !missingParent && !banned && !timedOut
            && (ownsResource || CanDo(effective.Manage, bagTokens));
        var reason = siteUnavailable ? "site-unavailable" : missingParent ? "tangent-not-found" : suspended ? "suspended"
            : banned ? "banned" : !read ? "role-required" : "allowed";
        return selected with
        {
            IsOwner = ownsResource,
            CanRead = read,
            CanWrite = write,
            CanManage = manage,
            CanAppointManagers = manage,
            Reason = reason
        };
    }

    /// <summary>Evaluates a Topic-level capability over the decided policy. EditOwnPost and
    /// DeleteOwnPost here are affordances only; authorship is decided by <see cref="EvaluatePost"/>.</summary>
    public static TopicPermissionDecision Evaluate(TopicPolicy? policy, TopicCapability capability)
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
    public static TopicPermissionDecision EvaluatePost(TopicPolicy? policy, TopicCapability capability, string? authorParticipantId, bool removed)
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

    private static TopicPermissionDecision ReadDecision(TopicPolicy policy, TopicCapability capability)
        => policy.CanRead
            ? Allow(policy, capability)
            : Deny(policy, capability, PolicyBoundaryReason(policy));

    private static TopicPermissionDecision ReportDecision(TopicPolicy policy, TopicCapability capability)
        => string.IsNullOrWhiteSpace(policy.ActorParticipantId)
            ? Deny(policy, capability, PermissionDeniedReason)
            : ReadDecision(policy, capability);

    private static TopicPermissionDecision WriteDecision(TopicPolicy policy, TopicCapability capability, bool editingRequired)
    {
        if (!policy.CanRead) return Deny(policy, capability, PolicyBoundaryReason(policy));
        if (policy.Locked) return Deny(policy, capability, TopicLockedReason);
        if (editingRequired && !policy.EditingAllowed) return Deny(policy, capability, EditingDisabledReason);
        if (!policy.CanWrite) return Deny(policy, capability, PolicyBoundaryReason(policy));
        return Allow(policy, capability);
    }

    private static TopicPermissionDecision RemoveDecision(TopicPolicy policy, TopicCapability capability)
    {
        if (!policy.CanManage) return Deny(policy, capability, ManageRequiredReason);
        if (!policy.CanRead) return Deny(policy, capability, PolicyBoundaryReason(policy));
        return Allow(policy, capability);
    }

    private static bool IsAuthor(string? actorParticipantId, string? authorParticipantId) =>
        !string.IsNullOrWhiteSpace(actorParticipantId)
        && !string.IsNullOrWhiteSpace(authorParticipantId)
        && string.Equals(actorParticipantId, authorParticipantId, StringComparison.Ordinal);

    private static string PolicyBoundaryReason(TopicPolicy policy) =>
        !string.IsNullOrWhiteSpace(policy.Reason) && !string.Equals(policy.Reason, AllowedReason, StringComparison.Ordinal)
            ? policy.Reason
            : PermissionDeniedReason;

    private static bool CanDo(IEnumerable<string>? tokens, IReadOnlyCollection<string> bagTokens)
        => (tokens ?? []).Select(TangentRoleAccess.ResolveDerived).Any(bagTokens.Contains);

    private static TopicPermissionDecision Allow(TopicPolicy policy, TopicCapability capability)
        => new(capability, true, AllowedReason, policy.SelectedPolicyRevision);

    private static TopicPermissionDecision Deny(TopicPolicy policy, TopicCapability capability, string reason)
        => new(capability, false, reason, policy.SelectedPolicyRevision);

    private static TopicPermissionDecision DenyNull(TopicCapability capability)
        => new(capability, false, PermissionDeniedReason, 0);
}
