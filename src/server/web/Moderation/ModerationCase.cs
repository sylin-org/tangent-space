using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core.Model;

namespace TangentSpace.Moderation;

/// <summary>A bounded case ledger. Testimony and the first non-punitive decisions live on one
/// entity so this slice has one durable source of truth and does not assume cross-entity atomicity.</summary>
public sealed class ModerationCase : Entity<ModerationCase>
{
    public const int MaximumTestimonies = 32;
    public const int MaximumDecisions = 32;
    public const int MaximumStatementLength = 512;
    public const int MaximumReasonCodeLength = 40;
    public const int MaximumDecisionReasonLength = 280;

    public string TangentKey { get; set; } = "";
    public string RoomKey { get; set; } = "";
    public string SubjectMessageId { get; set; } = "";
    public string SubjectParticipantId { get; set; } = "";
    public string State { get; set; } = ModerationCaseStates.Open;
    public long Revision { get; set; } = 1;
    public DateTimeOffset FirstReportedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? NextReviewAt { get; set; }
    public string? EscalatedToParticipantId { get; set; }
    public bool TestimonySaturated { get; set; }
    public bool DecisionSaturated { get; set; }
    public IReadOnlyList<ModerationTestimony> Testimonies { get; set; } = [];
    public IReadOnlyList<ModerationDecision> Decisions { get; set; } = [];

    public static string Key(string roomKey, string messageId)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"moderation-case\n{roomKey}\n{messageId}")));
}

public static class ModerationCaseStates
{
    public const string Open = "open";
    public const string Deferred = "deferred";
    public const string Escalated = "escalated";
}

public static class ModerationActions
{
    public const string Defer = "defer";
    public const string Escalate = "escalate";
}

public sealed record ModerationTestimony(string Id, string ReporterParticipantId, string OperationId,
    string ReasonCode, string Statement, string SubjectRevision, DateTimeOffset SubmittedAt);

public sealed record ModerationDecision(string OperationId, string ActorParticipantId, string Action,
    string Reason, DateTimeOffset? ReviewAfter, long ExpectedCaseRevision, string ExpectedSubjectRevision,
    long ResultingRevision, DateTimeOffset AppliedAt);

public sealed record ModerationCaseSummary(string CaseRef, string TopicRef, string SubjectPostRef,
    string State, long Revision, string SubjectRevision, bool SubjectAvailable, int TestimonyCount,
    bool TestimonySaturated, bool DecisionSaturated, DateTimeOffset FirstReportedAt,
    DateTimeOffset UpdatedAt, DateTimeOffset? NextReviewAt, string? EscalatedToParticipantRef,
    IReadOnlyList<string> AllowedActions);

public sealed record ModerationTestimonyView(string TestimonyRef, string ReasonCode, string Statement,
    string SubjectRevision, DateTimeOffset SubmittedAt);

public sealed record ModerationDecisionView(string ActorParticipantRef, string Action, string Reason,
    DateTimeOffset? ReviewAfter, long ExpectedCaseRevision, string ExpectedSubjectRevision,
    long ResultingRevision, DateTimeOffset AppliedAt);

public sealed record ModerationCaseView(ModerationCaseSummary Case,
    IReadOnlyList<ModerationTestimonyView> Testimonies, int TestimonyOffset, int? NextTestimonyOffset,
    IReadOnlyList<ModerationDecisionView> Decisions, bool DecisionsTruncated);

public sealed record ModerationCasePage(IReadOnlyList<ModerationCaseSummary> Cases, int Page,
    int? NextPage, bool AtLeast, bool Saturated);

public sealed record ModerationReportResult(ModerationCaseSummary Case, bool AlreadyReported, bool Accepted);

public sealed record ModerationDecisionPreview(ModerationCaseSummary Case, string Action, string Effect,
    bool Reversible, DateTimeOffset? ReviewAfter);

public sealed class ModerationCaseConflictException(string message) : Exception(message);
