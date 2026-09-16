namespace Tangent.Community;

/// <summary>A current decision, not a durable grant. Acceptance must execute inside the coordinator callback.</summary>
public sealed record TopicPolicy(
    string TopicKey,
    string? ActorParticipantId,
    long SelectedPolicyRevision,
    long SpacePolicyRevision,
    TopicAdmission Admission,
    TopicRole? Role,
    bool IsOwner,
    bool CanRead,
    bool CanWrite,
    bool CanManage,
    bool CanAppointManagers,
    string Reason,
    bool EditingAllowed = false,
    bool Locked = false);
