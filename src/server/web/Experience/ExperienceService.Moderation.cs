using System.Security.Claims;
using TangentSpace.Application;
using TangentSpace.Moderation;
using TangentSpace.Participation;

namespace TangentSpace.Experience;

public sealed partial class ExperienceService
{
    public async Task<ExperienceResponse> ReportPost(ClaimsPrincipal principal, string topicKey,
        string requestId, string postRef, string reasonCode, string statement, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        await activity.EnsureParticipantActive(participantId, credential, ct);
        var identity = await IdentityOf(participantId, ct);
        var parsed = refs.ParsePost(postRef);
        if (parsed is null || parsed.Value.TopicKey != topicKey
            || postRef != refs.Post(parsed.Value.TangentKey, parsed.Value.TopicKey, parsed.Value.PostId))
            throw new RequestArgumentException("postRef", "Copy a Post reference returned by this Topic.");
        return await receipts.Run(RegistryCredential(principal), participantId, requestId, async () =>
        {
            var registration = await receipts.Register(RegistryCredential(principal), participantId, requestId,
                "ReportPost", parsed.Value.PostId, new Dictionary<string, string?>
                {
                    ["postRef"] = postRef, ["reasonCode"] = reasonCode, ["statement"] = statement
                }, ct);
            // Domain replay is intentionally used for pending receipts: its embedded operation id
            // recovers a committed case update after a process failure between the two entities.
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("report_post", principal, identity, registration.Record, participantId, credential, ct);
            var operationId = registration.Record.NamespacedOperationId
                ?? OperationReceipt.BuildOperationId(RegistryCredential(principal), participantId, requestId);
            var result = await moderation.Report(participantId, topicKey, parsed.Value.PostId,
                operationId, reasonCode, statement, ct);
            // A reporter may submit testimony but is not thereby entitled to inspect the case,
            // its other testimony, its state, or the human escalation target. Persist the same
            // reporter-safe shape for receipt replay so GET operation cannot become a side door.
            var report = new ExperienceReportData(postRef, result.Accepted, result.AlreadyReported,
                result.Case.TestimonySaturated);
            var data = Serialize(report);
            await receipts.Complete(registration.Record, "completed", postRef, data, ct);
            var place = await TopicPlaceOf(principal, participantId, parsed.Value.TangentKey, topicKey, ct);
            return await Assemble("report_post", ExperienceStatus.Ok, identity, place,
                new ExperienceResult(report, new ExperienceReceipt(requestId, "completed", postRef, null), null),
                (await digest.Page(participantId, credential, null, parsed.Value.TangentKey, topicKey, 3, ct)).Attention,
                Empty(), [new(ExperienceActionNames.ReadTopic, place.TopicRef!, postRef, "Return to the reported Post")],
                null, CapabilitiesOf(place), participantId, credential, ct);
        }, ct);
    }

    public async Task<ExperienceResponse> ListModerationCases(ClaimsPrincipal principal, string topicKey,
        int page, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var identity = await IdentityOf(participantId, ct);
        var result = await moderation.List(participantId, topicKey, page, ct);
        var tangentKey = result.Cases.FirstOrDefault()?.TopicRef is { } topicRef
            && refs.ParseTopic(topicRef) is { } parsed ? parsed.TangentKey
            : (await topics.Describe(participantId, topicKey, ct))?.TangentKey
                ?? throw new UnauthorizedAccessException();
        var place = await TopicPlaceOf(principal, participantId, tangentKey, topicKey, ct);
        var actions = result.Cases.Take(3)
            .Select(item => new ExperienceAction(ExperienceActionNames.ReadModerationCase,
                item.CaseRef, null, "Read this moderation case")).ToList();
        return await Assemble("list_moderation_cases", ExperienceStatus.Ok, identity, place,
            new ExperienceResult(result, null, null),
            (await digest.Page(participantId, credential, null, tangentKey, topicKey, 3, ct)).Attention,
            Empty(), actions, null, new ExperienceCapabilities(true, false, true), participantId, credential, ct);
    }

    public async Task<ExperienceResponse> ReadModerationCase(ClaimsPrincipal principal, string caseId,
        int testimonyOffset, int testimonyLimit, int decisionLimit, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var identity = await IdentityOf(participantId, ct);
        var result = await moderation.Read(participantId, CaseId(caseId), testimonyOffset, testimonyLimit, decisionLimit, ct);
        var scope = refs.ParseTopic(result.Case.TopicRef) ?? throw new InvalidOperationException("The case scope is invalid.");
        var place = await TopicPlaceOf(principal, participantId, scope.TangentKey, scope.TopicKey, ct);
        var actions = result.Case.AllowedActions.Where(name => name is ExperienceActionNames.PreviewModerationAction
                or ExperienceActionNames.ApplyModerationAction)
            .Select(name => new ExperienceAction(name, result.Case.CaseRef, null,
                name == ExperienceActionNames.PreviewModerationAction ? "Preview a defer or escalation" : "Apply a defer or escalation"))
            .ToList();
        return await Assemble("read_moderation_case", ExperienceStatus.Ok, identity, place,
            new ExperienceResult(result, null, null),
            (await digest.Page(participantId, credential, null, scope.TangentKey, scope.TopicKey, 3, ct)).Attention,
            Empty(), actions, null, new ExperienceCapabilities(true, false, true), participantId, credential, ct);
    }

    public async Task<ExperienceResponse> PreviewModerationAction(ClaimsPrincipal principal, string caseId,
        string action, string summary, DateTimeOffset? deferredUntil, long expectedCaseRevision,
        string expectedSubjectRevision, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var identity = await IdentityOf(participantId, ct);
        var result = await moderation.Preview(participantId, CaseId(caseId), action, summary,
            deferredUntil, expectedCaseRevision, expectedSubjectRevision, ct);
        var scope = refs.ParseTopic(result.Case.TopicRef) ?? throw new InvalidOperationException("The case scope is invalid.");
        var place = await TopicPlaceOf(principal, participantId, scope.TangentKey, scope.TopicKey, ct);
        return await Assemble("preview_moderation_action", ExperienceStatus.Ok, identity, place,
            new ExperienceResult(result, null, null),
            (await digest.Page(participantId, credential, null, scope.TangentKey, scope.TopicKey, 3, ct)).Attention,
            Empty(), [new(ExperienceActionNames.ApplyModerationAction, result.Case.CaseRef, null, "Apply this reviewed decision")],
            null, new ExperienceCapabilities(true, false, true), participantId, credential, ct);
    }

    public async Task<ExperienceResponse> ApplyModerationAction(ClaimsPrincipal principal, string caseId,
        string requestId, string action, string summary, DateTimeOffset? deferredUntil,
        long expectedCaseRevision, string expectedSubjectRevision, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var identity = await IdentityOf(participantId, ct);
        var rawCaseId = CaseId(caseId);
        // Reauthorize before even replaying presentation from the request registry.
        await moderation.Read(participantId, rawCaseId, 0, 1, 1, ct);
        return await receipts.Run(RegistryCredential(principal), participantId, requestId, async () =>
        {
            var registration = await receipts.Register(RegistryCredential(principal), participantId, requestId,
                "ApplyModerationAction", rawCaseId, new Dictionary<string, string?>
                {
                    ["action"] = action, ["summary"] = summary, ["deferredUntil"] = deferredUntil?.ToString("O"),
                    ["expectedCaseRevision"] = expectedCaseRevision.ToString(),
                    ["expectedSubjectRevision"] = expectedSubjectRevision
                }, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("apply_moderation_action", principal, identity, registration.Record, participantId, credential, ct);
            var operationId = registration.Record.NamespacedOperationId
                ?? OperationReceipt.BuildOperationId(RegistryCredential(principal), participantId, requestId);
            var result = await moderation.Decide(participantId, rawCaseId, operationId, action, summary,
                deferredUntil, expectedCaseRevision, expectedSubjectRevision, ct);
            var data = Serialize(result);
            await receipts.Complete(registration.Record, "completed", result.Case.CaseRef, data, ct);
            var scope = refs.ParseTopic(result.Case.TopicRef) ?? throw new InvalidOperationException("The case scope is invalid.");
            var place = await TopicPlaceOf(principal, participantId, scope.TangentKey, scope.TopicKey, ct);
            return await Assemble("apply_moderation_action", ExperienceStatus.Ok, identity, place,
                new ExperienceResult(result, new ExperienceReceipt(requestId, "completed", result.Case.CaseRef, null), null),
                (await digest.Page(participantId, credential, null, scope.TangentKey, scope.TopicKey, 3, ct)).Attention,
                Empty(), [], null, new ExperienceCapabilities(true, false, true), participantId, credential, ct);
        }, ct);
    }

    private static ExperienceCapabilities CapabilitiesOf(ExperiencePlace place)
        => new(true, false, place.AllowedActions.Contains(ExperienceActionNames.ListModerationCases));

    private static string CaseId(string value)
    {
        if (value.Length == 64
            && value.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')) return value;
        throw new RequestArgumentException("caseRef", "Copy a moderation case reference returned by this server.");
    }
}
