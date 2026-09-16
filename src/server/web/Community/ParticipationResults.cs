using Tangent.Stewardship;

namespace Tangent.Community;

public enum JoinOutcome { Joined, AlreadyMember, PendingApproval }

public enum LeaveOutcome { Left, AlreadyNotMember }

public sealed record JoinResult(JoinOutcome Outcome, TangentMembershipResult? Membership, string? PendingRequestId);

public sealed record LeaveResult(LeaveOutcome Outcome, TangentMembershipResult? Membership);

public sealed record TangentInvitationResult(string InvitationId, string TangentKey, string RecipientParticipantId, TangentRole GrantedRole,
    DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, bool Revoked, bool Redeemed, bool Delivered);

public sealed record TangentJoinRequestSummary(string RequestId, string TangentKey, string ParticipantId,
    DateTimeOffset RequestedAt, bool Decided, bool? Accepted);

public sealed record TangentJoinRequestDecision(string RequestId, bool Accepted, TangentMembershipResult? Membership);

public sealed record RoleChangeResult(TangentMembershipResult? Tangent, TopicAdministrationResult? Topic);

public sealed record TangentPolicyResult(string TangentKey, TangentAdmission Admission, ParticipationPreset Preset,
    UndeclaredAccess Undeclared, long PolicyRevision);
