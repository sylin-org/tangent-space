using TangentSpace.Rooms;

namespace TangentSpace.Communities;

public enum CompanionJoinOutcome { Joined, AlreadyMember, PendingApproval }

public enum CompanionLeaveOutcome { Left, AlreadyNotMember }

public sealed record CompanionJoinResult(CompanionJoinOutcome Outcome, TangentMembershipResult? Membership, string? PendingRequestId);

public sealed record CompanionLeaveResult(CompanionLeaveOutcome Outcome, TangentMembershipResult? Membership);

public sealed record TangentInvitationResult(string InvitationId, string TangentKey, string RecipientParticipantId, TangentRole GrantedRole,
    DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, bool Revoked, bool Redeemed, bool Delivered);

public sealed record TangentJoinRequestSummary(string RequestId, string TangentKey, string ParticipantId,
    DateTimeOffset RequestedAt, bool Decided, bool? Accepted);

public sealed record TangentJoinRequestDecision(string RequestId, bool Accepted, TangentMembershipResult? Membership);

public sealed record CompanionRoleResult(TangentMembershipResult? Community, TopicAdministrationResult? Channel);

public sealed record TangentPolicyResult(string TangentKey, TangentAdmission Admission, ParticipationPreset Preset,
    UndeclaredAccess Undeclared, long PolicyRevision);
