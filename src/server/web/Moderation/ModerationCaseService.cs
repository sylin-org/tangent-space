using System.Security.Cryptography;
using System.Text;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using TangentSpace.Authorization;
using TangentSpace.Conversation;
using TangentSpace.Infrastructure;
using TangentSpace.Mcp;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Moderation;

/// <summary>First accountable case path: report, bounded steward read, defer, or escalate.
/// It deliberately cannot conceal content or sanction a participant.</summary>
public sealed class ModerationCaseService(RoomGovernance rooms, McpRefs refs, TimeProvider clock)
{
    public const int PageSize = 10;
    public const int MaximumPage = 10_000;
    public const int MaximumTestimonyPage = 8;
    public const int MaximumDecisionPage = 8;
    public static readonly TimeSpan MaximumDeferral = TimeSpan.FromDays(7);
    private static readonly QueryDefinition queue = new()
    {
        Sort = SortSpecParser.ParseStrict<ModerationCase>(nameof(ModerationCase.FirstReportedAt))
    };

    public Task<ModerationReportResult> Report(string actorId, string roomKey, string messageId,
        string operationId, string reasonCode, string statement, CancellationToken ct)
    {
        CheckOperation(operationId);
        CheckReasonCode(reasonCode);
        CheckText(statement, nameof(statement), ModerationCase.MaximumStatementLength);
        return rooms.WithCurrentPolicy(actorId, roomKey, async (policy, token) =>
        {
            var message = await Message.Get(messageId, token);
            if (message is null || message.RoomKey != roomKey)
                throw new ArgumentException("Choose a Post in this Topic.", nameof(messageId));
            Require(TopicPermissionEvaluator.Evaluate(policy, TopicCapability.Read),
                "The current Topic rules do not allow reporting this Post.");
            var now = clock.GetUtcNow();
            var id = ModerationCase.Key(roomKey, messageId);
            var current = await ModerationCase.Get(id, token);
            var subjectRevision = SubjectRevision(message);
            var replay = current?.Testimonies.FirstOrDefault(item => item.OperationId == operationId);
            if (replay is not null)
            {
                if (replay.ReporterParticipantId != actorId || replay.ReasonCode != reasonCode
                    || replay.Statement != statement.Trim())
                    throw new ModerationCaseConflictException("That operation already identifies a different report.");
                return new ModerationReportResult(Summary(current!, message, policy), AlreadyReported: false, Accepted: true);
            }
            Require(TopicPermissionEvaluator.EvaluatePost(policy, TopicCapability.ReportPost,
                message.AuthorParticipantId, message.Removed), "The current Topic rules do not allow reporting this Post.");
            if (current is null)
            {
                var room = await Room.Get(roomKey, token)
                    ?? throw new ArgumentException("Choose a Post in this Topic.", nameof(messageId));
                current = new ModerationCase
                {
                    Id = id, TangentKey = room.TangentKey, RoomKey = roomKey,
                    SubjectMessageId = messageId, SubjectParticipantId = message.AuthorParticipantId,
                    FirstReportedAt = now, UpdatedAt = now
                };
            }
            if (current.Testimonies.Any(item => item.ReporterParticipantId == actorId))
                return new ModerationReportResult(Summary(current, message, policy), AlreadyReported: true, Accepted: false);
            if (current.Testimonies.Count >= ModerationCase.MaximumTestimonies)
                return new ModerationReportResult(Summary(current, message, policy), AlreadyReported: false, Accepted: false);
            var testimony = new ModerationTestimony(
                Hash($"testimony\n{id}\n{actorId}\n{operationId}"), actorId, operationId,
                reasonCode, statement.Trim(), subjectRevision, now);
            var existed = current.Testimonies.Count > 0;
            current.Testimonies = [.. current.Testimonies, testimony];
            current.TestimonySaturated = current.Testimonies.Count >= ModerationCase.MaximumTestimonies;
            if (existed) current.Revision = checked(current.Revision + 1);
            if (current.State == ModerationCaseStates.Deferred)
            {
                current.State = ModerationCaseStates.Open;
                current.NextReviewAt = null;
            }
            current.UpdatedAt = now;
            await current.Save(token);
            return new ModerationReportResult(Summary(current, message, policy), AlreadyReported: false, Accepted: true);
        }, ct);
    }

    public Task<ModerationCasePage> List(string actorId, string roomKey, int page, CancellationToken ct)
    {
        if (page is < 1 or > MaximumPage)
            throw new ArgumentException("Choose a case page between 1 and 10000.", nameof(page));
        return rooms.WithCurrentPolicy(actorId, roomKey, async (policy, token) =>
        {
            RequireSteward(policy);
            var selected = (await ModerationCase.Query(item => item.RoomKey == roomKey,
                queue.WithPagination(page, PageSize).WithCountStrategy(null), token)).ToList();
            var hasNext = page < MaximumPage && selected.Count == PageSize && (await ModerationCase.Query(item => item.RoomKey == roomKey,
                queue.WithPagination(page + 1, PageSize).WithCountStrategy(null), token)).Count > 0;
            var summaries = new List<ModerationCaseSummary>(selected.Count);
            foreach (var item in selected)
                summaries.Add(Summary(item, await SubjectOrNull(item, token), policy));
            return new ModerationCasePage(summaries, page, hasNext ? page + 1 : null,
                AtLeast: hasNext,
                Saturated: summaries.Any(item => item.TestimonySaturated || item.DecisionSaturated));
        }, ct);
    }

    public async Task<ModerationCaseView> Read(string actorId, string caseId, int testimonyOffset,
        int testimonyLimit, int decisionLimit, CancellationToken ct)
    {
        ValidateProjection(testimonyOffset, testimonyLimit, decisionLimit);
        var seed = await Load(caseId, ct);
        return await rooms.WithCurrentPolicy(actorId, seed.RoomKey, async (policy, token) =>
        {
            RequireSteward(policy);
            var current = await ModerationCase.Get(caseId, token)
                ?? throw Unavailable();
            var testimonies = current.Testimonies.Skip(testimonyOffset).Take(testimonyLimit)
                .Select(value => new ModerationTestimonyView("testimony:" + value.Id, value.ReasonCode,
                    value.Statement, value.SubjectRevision, value.SubmittedAt)).ToList();
            var next = testimonyOffset + testimonies.Count < current.Testimonies.Count
                ? testimonyOffset + testimonies.Count : (int?)null;
            var decisions = current.Decisions.TakeLast(decisionLimit)
                .Select(value => new ModerationDecisionView(value.ActorParticipantId, value.Action,
                    value.Reason, value.ReviewAfter, value.ExpectedCaseRevision,
                    value.ExpectedSubjectRevision, value.ResultingRevision, value.AppliedAt)).ToList();
            return new ModerationCaseView(Summary(current, await SubjectOrNull(current, token), policy),
                testimonies, testimonyOffset, next, decisions, current.Decisions.Count > decisions.Count);
        }, ct);
    }

    public async Task<ModerationDecisionPreview> Preview(string actorId, string caseId, string action,
        string reason, DateTimeOffset? reviewAfter, long expectedCaseRevision,
        string expectedSubjectRevision, CancellationToken ct)
    {
        CheckText(reason, nameof(reason), ModerationCase.MaximumDecisionReasonLength);
        var seed = await Load(caseId, ct);
        return await rooms.WithCurrentPolicy(actorId, seed.RoomKey, async (policy, token) =>
        {
            RequireSteward(policy);
            var current = await ModerationCase.Get(caseId, token) ?? throw Unavailable();
            if (current.Revision != expectedCaseRevision) throw StaleCase();
            var subject = await CurrentSubject(current, expectedSubjectRevision, token);
            var until = ValidateTransition(current, action, reviewAfter, clock.GetUtcNow());
            return new ModerationDecisionPreview(Summary(current, subject, policy), action,
                action == ModerationActions.Defer
                    ? $"Return this case to the steward queue after {until:O}."
                    : "Escalate this case to the accountable human Host owner; no sanction is applied.",
                Reversible: action == ModerationActions.Defer, until);
        }, ct);
    }

    public async Task<ModerationCaseView> Decide(string actorId, string caseId, string operationId,
        string action, string reason, DateTimeOffset? reviewAfter, long expectedCaseRevision,
        string expectedSubjectRevision, CancellationToken ct)
    {
        CheckOperation(operationId);
        CheckText(reason, nameof(reason), ModerationCase.MaximumDecisionReasonLength);
        var seed = await Load(caseId, ct);
        return await rooms.WithCurrentPolicy(actorId, seed.RoomKey, async (policy, token) =>
        {
            RequireSteward(policy);
            var current = await ModerationCase.Get(caseId, token) ?? throw Unavailable();
            var cleanReason = reason.Trim();
            var replay = current.Decisions.FirstOrDefault(item => item.OperationId == operationId);
            if (replay is not null)
            {
                if (replay.ActorParticipantId != actorId || replay.Action != action || replay.Reason != cleanReason
                    || replay.ReviewAfter != reviewAfter || replay.ExpectedCaseRevision != expectedCaseRevision
                    || replay.ExpectedSubjectRevision != expectedSubjectRevision)
                    throw new ModerationCaseConflictException("That operation already identifies a different case decision.");
                return await ReadWithin(current, policy, token);
            }
            if (current.Revision != expectedCaseRevision) throw StaleCase();
            var subject = await CurrentSubject(current, expectedSubjectRevision, token);
            var now = clock.GetUtcNow();
            var until = ValidateTransition(current, action, reviewAfter, now);
            if (current.Decisions.Count >= ModerationCase.MaximumDecisions)
                throw new InvalidOperationException("This case reached its bounded decision history; involve the human operator.");
            var nextRevision = checked(current.Revision + 1);
            current.Decisions = [.. current.Decisions,
                new ModerationDecision(operationId, actorId, action, cleanReason, until,
                    expectedCaseRevision, expectedSubjectRevision, nextRevision, now)];
            current.DecisionSaturated = current.Decisions.Count >= ModerationCase.MaximumDecisions;
            current.Revision = nextRevision;
            current.State = action == ModerationActions.Defer ? ModerationCaseStates.Deferred : ModerationCaseStates.Escalated;
            current.NextReviewAt = until;
            if (action == ModerationActions.Escalate)
                current.EscalatedToParticipantId = (await TangentSite.Get(TangentConstants.SiteId, token))?.OwnerParticipantId
                    ?? throw new InvalidOperationException("The accountable Host owner is unavailable.");
            current.UpdatedAt = now;
            await current.Save(token);
            return await ReadWithin(current, policy, token, subject);
        }, ct);
    }

    private async Task<ModerationCaseView> ReadWithin(ModerationCase current, RoomPolicy policy,
        CancellationToken ct, Message? subject = null)
    {
        subject ??= await SubjectOrNull(current, ct);
        var decisions = current.Decisions.TakeLast(MaximumDecisionPage)
            .Select(value => new ModerationDecisionView(value.ActorParticipantId, value.Action, value.Reason,
                value.ReviewAfter, value.ExpectedCaseRevision, value.ExpectedSubjectRevision,
                value.ResultingRevision, value.AppliedAt)).ToList();
        var testimony = current.Testimonies.Take(MaximumTestimonyPage)
            .Select(value => new ModerationTestimonyView("testimony:" + value.Id, value.ReasonCode,
                value.Statement, value.SubjectRevision, value.SubmittedAt)).ToList();
        return new ModerationCaseView(Summary(current, subject, policy), testimony, 0,
            current.Testimonies.Count > testimony.Count ? testimony.Count : null,
            decisions, current.Decisions.Count > decisions.Count);
    }

    private ModerationCaseSummary Summary(ModerationCase item, Message? subject, RoomPolicy policy)
        => new(refs.Case(item.TangentKey, item.RoomKey, item.Id), refs.Channel(item.TangentKey, item.RoomKey),
            refs.Message(item.TangentKey, item.RoomKey, item.SubjectMessageId), item.State, item.Revision,
            subject is null ? "unavailable" : SubjectRevision(subject),
            subject is not null && subject.RoomKey == item.RoomKey && !subject.Removed,
            item.Testimonies.Count, item.TestimonySaturated, item.DecisionSaturated,
            item.FirstReportedAt, item.UpdatedAt, item.NextReviewAt, item.EscalatedToParticipantId,
            StewardActions(policy, item));

    private static IReadOnlyList<string> StewardActions(RoomPolicy policy, ModerationCase item)
    {
        if (!TopicPermissionEvaluator.Evaluate(policy, TopicCapability.Read).Allowed
            || !TopicPermissionEvaluator.Evaluate(policy, TopicCapability.ManageTopic).Allowed) return [];
        return item.State == ModerationCaseStates.Escalated
            ? ["read_moderation_case"]
            : ["read_moderation_case", "preview_moderation_action", "apply_moderation_action"];
    }

    private static void RequireSteward(RoomPolicy policy)
    {
        Require(TopicPermissionEvaluator.Evaluate(policy, TopicCapability.Read),
            "The current Topic rules do not allow reading this moderation case.");
        Require(TopicPermissionEvaluator.Evaluate(policy, TopicCapability.ManageTopic),
            "Only a current Topic steward can inspect or decide moderation cases.");
    }

    private static void Require(TopicPermissionDecision decision, string message)
    {
        if (!decision.Allowed) throw new UnauthorizedAccessException(message);
    }

    private static DateTimeOffset? ValidateTransition(ModerationCase current, string action,
        DateTimeOffset? reviewAfter, DateTimeOffset now)
    {
        if (current.State == ModerationCaseStates.Escalated)
            throw new ModerationCaseConflictException("This case is already with the accountable human owner.");
        if (action == ModerationActions.Escalate)
        {
            if (reviewAfter is not null) throw new ArgumentException("Escalation does not carry a review time.");
            return null;
        }
        if (action != ModerationActions.Defer)
            throw new ArgumentException("Choose defer or escalate.", nameof(action));
        if (reviewAfter is not { } until || until <= now || until > now + MaximumDeferral)
            throw new ArgumentException("Choose a review time within the next seven days.", nameof(reviewAfter));
        return until;
    }

    private static async Task<Message> CurrentSubject(ModerationCase current, string expectedRevision, CancellationToken ct)
    {
        var message = await SubjectOrNull(current, ct);
        if (message is null || message.Removed || SubjectRevision(message) != expectedRevision)
            throw new ModerationCaseConflictException("The reported Post changed or became unavailable. Read the case again before deciding.");
        return message;
    }

    private static async Task<Message?> SubjectOrNull(ModerationCase current, CancellationToken ct)
    {
        var message = await Message.Get(current.SubjectMessageId, ct);
        return message?.RoomKey == current.RoomKey ? message : null;
    }

    private static string SubjectRevision(Message message)
        => Hash(string.Join('\n', "moderation-subject", message.Id, message.RoomKey, message.SourceCid,
            message.ChangeId ?? "", message.EditedAt?.ToString("O") ?? "", message.Removed.ToString(),
            message.RemovedAt?.ToString("O") ?? "", message.Content.CreatedAt.ToString("O"),
            message.Content.ReplyTo?.Uri ?? "", message.Content.ReplyTo?.Cid ?? "", message.Content.Text,
            string.Join('|', message.Facets?.Select(facet => facet.Canonical()) ?? [])));

    private static string Hash(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static async Task<ModerationCase> Load(string caseId, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        return await ModerationCase.Get(caseId, ct) ?? throw Unavailable();
    }

    private static void ValidateProjection(int testimonyOffset, int testimonyLimit, int decisionLimit)
    {
        if (testimonyOffset < 0) throw new ArgumentException("Testimony offset cannot be negative.");
        if (testimonyLimit is < 1 or > MaximumTestimonyPage)
            throw new ArgumentException("Choose 1–8 testimonies.");
        if (decisionLimit is < 1 or > MaximumDecisionPage)
            throw new ArgumentException("Choose 1–8 decisions.");
    }

    private static void CheckOperation(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException("Use a bounded operation identifier.", nameof(value));
    }

    private static void CheckReasonCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > ModerationCase.MaximumReasonCodeLength
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
            throw new ArgumentException("Use a reasonCode of 1–40 letters, digits, dots, hyphens or underscores.", nameof(value));
    }

    private static void CheckText(string value, string field, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Contains('\0'))
            throw new ArgumentException($"{field} must contain 1–{maximum} characters and no null characters.", field);
    }

    private static ArgumentException Unavailable()
        => new("That moderation case is unavailable.");
    private static ModerationCaseConflictException StaleCase()
        => new("The moderation case changed. Read its current revision before deciding.");
}
