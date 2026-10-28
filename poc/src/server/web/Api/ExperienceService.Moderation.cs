using System.Security.Claims;
using Tangent.Application;
using Tangent.Stewardship;
using Tangent.Identity;

namespace Tangent.Api;

public sealed partial class ExperienceService
{
    public async Task<ResourceResult<ExperienceReportData>> ReportPost(ClaimsPrincipal principal, string topicKey,
        string requestId, string postRef, string reasonCode, string statement, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var parsed = refs.ParsePost(postRef);
        if (parsed is null || parsed.Value.TopicKey != topicKey
            || postRef != refs.Post(parsed.Value.TangentKey, parsed.Value.TopicKey, parsed.Value.PostId))
            throw new RequestArgumentException("postRef", "Copy a Post reference returned by this Topic.");
        // The requestId names the testimony inside its case: the same id replays that testimony,
        // a different report under it is the case's own conflict.
        var operationId = requestId ?? "report-" + Guid.CreateVersion7().ToString("n");
        var result = await moderation.Report(participantId, topicKey, parsed.Value.PostId,
            operationId, reasonCode, statement, ct);
        // A reporter may submit testimony but is not thereby entitled to inspect the case,
        // its other testimony, its state, or the human escalation target.
        var report = new ExperienceReportData(postRef, result.Accepted, result.AlreadyReported,
            result.Case.TestimonySaturated);
        return ResourceResult<ExperienceReportData>.Success(report);
    }

    public async Task<ResourceResult<ModerationCasePage>> ListModerationCases(ClaimsPrincipal principal, string topicKey,
        int page, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var result = await moderation.List(participantId, topicKey, page, ct);
        return ResourceResult<ModerationCasePage>.Success(result);
    }

    public async Task<ResourceResult<ModerationCaseView>> ReadModerationCase(ClaimsPrincipal principal, string caseId,
        int testimonyOffset, int testimonyLimit, int decisionLimit, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var result = await moderation.Read(participantId, CaseId(caseId), testimonyOffset, testimonyLimit, decisionLimit, ct);
        if (TransportGrant(principal, ParticipationGrants.Manage) && await ModerationCase.Get(caseId, ct) is { } evidence)
        {
            var actions = await users.ActionsForCase(participantId, evidence, ct);
            if (actions.Count > 0)
                result = result with { ParticipantActions = new(evidence.SubjectParticipantId,
                    await hub.Directory.LabelOf(evidence.SubjectParticipantId, ct) ?? "Reported participant", actions) };
        }
        return ResourceResult<ModerationCaseView>.Success(result);
    }

    public async Task<ResourceResult<ModerationDecisionPreview>> PreviewModerationAction(ClaimsPrincipal principal, string caseId,
        string action, string summary, DateTimeOffset? deferredUntil, long expectedCaseRevision,
        string expectedSubjectRevision, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var result = await moderation.Preview(participantId, CaseId(caseId), action, summary,
            deferredUntil, expectedCaseRevision, expectedSubjectRevision, ct);
        return ResourceResult<ModerationDecisionPreview>.Success(result);
    }

    public async Task<ResourceResult<ModerationCaseView>> ApplyModerationAction(ClaimsPrincipal principal, string caseId,
        string requestId, string action, string summary, DateTimeOffset? deferredUntil,
        long expectedCaseRevision, string expectedSubjectRevision, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Manage);
        var rawCaseId = CaseId(caseId);
        // Reauthorize before deciding; the decision itself names its steward inside the case.
        await moderation.Read(participantId, rawCaseId, 0, 1, 1, ct);
        var operationId = requestId ?? "decision-" + Guid.CreateVersion7().ToString("n");
        var result = await moderation.Decide(participantId, rawCaseId, operationId, action, summary,
            deferredUntil, expectedCaseRevision, expectedSubjectRevision, ct);
        return ResourceResult<ModerationCaseView>.Success(result);
    }

    private static string CaseId(string value)
    {
        if (value.Length == 64
            && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')) return value;
        throw new RequestArgumentException("caseRef", "Copy a moderation case reference returned by this server.");
    }
}
