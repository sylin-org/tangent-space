using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using TangentSpace.Conversation;
using TangentSpace.Moderation;
using TangentSpace.Rooms;
using TangentSpace.Tests.ExperienceIntegration;
using Xunit;

namespace TangentSpace.Tests;

[Collection("Experience integration")]
public sealed class ModerationCaseTests : IAsyncLifetime
{
    private ExperienceWebApp app = null!;
    private ModerationCaseService Cases => app.Services.GetRequiredService<ModerationCaseService>();
    private RoomGovernance Rooms => app.Services.GetRequiredService<RoomGovernance>();
    private const string Topic = ExperienceWebApp.TopicKey;
    private const string Subject = "m-leo-m1";

    public async ValueTask InitializeAsync() => app = await ExperienceWebApp.StartAsync();
    public async ValueTask DisposeAsync() => await app.DisposeAsync();

    [Fact]
    public async Task Reports_group_distinct_testimony_without_repeat_reporter_amplification()
    {
        var first = await Cases.Report(app.AgentParticipantId, Topic, Subject, "report-agent-1",
            "harassment", "Please review the tone in this Post.", CancellationToken.None);
        Assert.True(first.Accepted);
        Assert.False(first.AlreadyReported);
        Assert.Equal(1, first.Case.TestimonyCount);

        var replay = await Cases.Report(app.AgentParticipantId, Topic, Subject, "report-agent-1",
            "harassment", "Please review the tone in this Post.", CancellationToken.None);
        Assert.True(replay.Accepted);
        Assert.Equal(first.Case.Revision, replay.Case.Revision);
        await Assert.ThrowsAsync<ModerationCaseConflictException>(() => Cases.Report(
            app.AgentParticipantId, Topic, Subject, "report-agent-1", "other",
            "The same operation cannot change its allegation.", CancellationToken.None));

        var repeated = await Cases.Report(app.AgentParticipantId, Topic, Subject, "report-agent-2",
            "other", "A replacement allegation must not overwrite the first.", CancellationToken.None);
        Assert.True(repeated.AlreadyReported);
        Assert.False(repeated.Accepted);
        Assert.Equal(first.Case.Revision, repeated.Case.Revision);

        var grouped = await Cases.Report(app.OwnerParticipantId, Topic, Subject, "report-owner-1",
            "harassment", "I observed the same exchange.", CancellationToken.None);
        Assert.True(grouped.Accepted);
        Assert.Equal(2, grouped.Case.TestimonyCount);
        Assert.Equal(first.Case.Revision + 1, grouped.Case.Revision);

        var detail = await Cases.Read(app.OwnerParticipantId, ModerationCase.Key(Topic, Subject), 0, 8, 8, CancellationToken.None);
        Assert.Equal(2, detail.Testimonies.Count);
        Assert.All(detail.Testimonies, item => Assert.StartsWith("testimony:", item.TestimonyRef));
        Assert.DoesNotContain(app.AgentParticipantId, JsonSerializer.Serialize(detail));
        await Assert.ThrowsAsync<ArgumentException>(() => Cases.Read(app.OwnerParticipantId,
            ModerationCase.Key(Topic, Subject), 0, 9, 8, CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_decisions_have_one_winner_and_one_explicit_conflict()
    {
        var report = await Cases.Report(app.AgentParticipantId, Topic, Subject, "report-race",
            "conduct", "Review this once even when two stewards act together.", CancellationToken.None);
        var assigned = await Rooms.SetMembership(app.OwnerParticipantId, Topic,
            ExperienceWebApp.AgentDid, RoomRole.Manager, CancellationToken.None);
        Assert.True(assigned.Accepted, assigned.Reason);
        var reviewAfter = DateTimeOffset.UtcNow.AddHours(1);

        var attempts = new[]
        {
            Cases.Decide(app.OwnerParticipantId, ModerationCase.Key(Topic, Subject), "race-owner",
                ModerationActions.Defer, "Owner decision.", reviewAfter,
                report.Case.Revision, report.Case.SubjectRevision, CancellationToken.None),
            Cases.Decide(app.AgentParticipantId, ModerationCase.Key(Topic, Subject), "race-agent",
                ModerationActions.Escalate, "Agent decision.", null,
                report.Case.Revision, report.Case.SubjectRevision, CancellationToken.None)
        };
        try { await Task.WhenAll(attempts); }
        catch { /* Assert the individual outcomes below. */ }

        _ = Assert.Single(attempts, attempt => attempt.IsCompletedSuccessfully);
        _ = Assert.Single(attempts,
            attempt => attempt.Exception?.InnerExceptions.Any(error => error is ModerationCaseConflictException) == true);
        var stored = await ModerationCase.Get(ModerationCase.Key(Topic, Subject));
        Assert.NotNull(stored);
        Assert.Single(stored.Decisions);
        Assert.Equal(report.Case.Revision + 1, stored.Revision);
    }

    [Fact]
    public async Task Preview_is_read_only_and_defer_replay_is_one_decision()
    {
        var report = await Cases.Report(app.AgentParticipantId, Topic, Subject, "report-preview",
            "conduct", "This deserves a calm second look.", CancellationToken.None);
        var reviewAfter = DateTimeOffset.UtcNow.AddHours(1);
        var preview = await Cases.Preview(app.OwnerParticipantId, ModerationCase.Key(Topic, Subject),
            ModerationActions.Defer, "Wait for more context.", reviewAfter,
            report.Case.Revision, report.Case.SubjectRevision, CancellationToken.None);
        Assert.True(preview.Reversible);
        var afterPreview = await ModerationCase.Get(ModerationCase.Key(Topic, Subject));
        Assert.NotNull(afterPreview);
        Assert.Empty(afterPreview.Decisions);
        Assert.Equal(report.Case.Revision, afterPreview.Revision);

        var applied = await Cases.Decide(app.OwnerParticipantId, afterPreview.Id, "decision-defer",
            ModerationActions.Defer, "Wait for more context.", reviewAfter,
            report.Case.Revision, report.Case.SubjectRevision, CancellationToken.None);
        Assert.Equal(ModerationCaseStates.Deferred, applied.Case.State);
        Assert.Single(applied.Decisions);

        var replay = await Cases.Decide(app.OwnerParticipantId, afterPreview.Id, "decision-defer",
            ModerationActions.Defer, "Wait for more context.", reviewAfter,
            report.Case.Revision, report.Case.SubjectRevision, CancellationToken.None);
        Assert.Single(replay.Decisions);
        Assert.Equal(applied.Case.Revision, replay.Case.Revision);

        var reopened = await Cases.Report(app.OwnerParticipantId, Topic, Subject, "report-reopen",
            "context", "New testimony should bring a deferred case back.", CancellationToken.None);
        Assert.Equal(ModerationCaseStates.Open, reopened.Case.State);
        var escalated = await Cases.Decide(app.OwnerParticipantId, afterPreview.Id, "decision-escalate",
            ModerationActions.Escalate, "The accountable human should review this.", null,
            reopened.Case.Revision, reopened.Case.SubjectRevision, CancellationToken.None);
        Assert.Equal(ModerationCaseStates.Escalated, escalated.Case.State);
        Assert.Equal(app.OwnerParticipantId, escalated.Case.EscalatedToParticipantRef);
        await Assert.ThrowsAsync<ModerationCaseConflictException>(() => Cases.Decide(
            app.OwnerParticipantId, afterPreview.Id, "decision-after-escalation", ModerationActions.Defer,
            "Do not downgrade escalation.", DateTimeOffset.UtcNow.AddHours(1), escalated.Case.Revision,
            escalated.Case.SubjectRevision, CancellationToken.None));
    }

    [Fact]
    public async Task Subject_edit_and_stale_case_revision_are_explicit_conflicts()
    {
        var report = await Cases.Report(app.AgentParticipantId, Topic, Subject, "report-stale",
            "conduct", "Review the current revision.", CancellationToken.None);
        await Assert.ThrowsAsync<ModerationCaseConflictException>(() => Cases.Preview(
            app.OwnerParticipantId, ModerationCase.Key(Topic, Subject), ModerationActions.Escalate,
            "Stale case version.", null, report.Case.Revision + 1, report.Case.SubjectRevision, CancellationToken.None));

        using (EntityContext.NoCache())
        {
            var message = await Message.Get(Subject);
            Assert.NotNull(message);
            message.Content = message.Content with { Text = message.Content.Text + " edited" };
            message.EditedAt = DateTimeOffset.UtcNow;
            await message.Save();
        }
        await Assert.ThrowsAsync<ModerationCaseConflictException>(() => Cases.Preview(
            app.OwnerParticipantId, ModerationCase.Key(Topic, Subject), ModerationActions.Escalate,
            "The Post changed.", null, report.Case.Revision, report.Case.SubjectRevision, CancellationToken.None));
    }

    [Fact]
    public async Task Http_profile_is_permission_shaped_and_saved_apply_is_denied_after_demotion()
    {
        using var agentTopic = await app.Http.GetAsync($"/api/v1/experience/topics/{Topic}");
        Assert.Equal(HttpStatusCode.OK, agentTopic.StatusCode);
        using (var json = JsonDocument.Parse(await agentTopic.Content.ReadAsStringAsync()))
        {
            Assert.False(json.RootElement.GetProperty("capabilities").GetProperty("stewardship").GetBoolean());
            Assert.DoesNotContain(json.RootElement.GetProperty("place").GetProperty("allowedActions").EnumerateArray(),
                item => item.GetString() == "list_moderation_cases");
        }

        var postRef = $"{app.Origin}::{ExperienceWebApp.TangentKey}::{Topic}::{Subject}";
        using var report = await app.Http.PostAsJsonAsync($"/api/v1/experience/topics/{Topic}/reports", new
        {
            requestId = "http-report", postRef, reasonCode = "conduct", statement = "Please review this exchange."
        });
        Assert.True(report.StatusCode == HttpStatusCode.OK,
            $"report failed: {(int)report.StatusCode} {await report.Content.ReadAsStringAsync()}");
        using var reportJson = JsonDocument.Parse(await report.Content.ReadAsStringAsync());
        var reportBody = reportJson.RootElement.GetProperty("result");
        var reportData = reportBody.GetProperty("data");
        Assert.Equal(postRef, reportData.GetProperty("postRef").GetString());
        Assert.True(reportData.GetProperty("accepted").GetBoolean());
        Assert.False(reportData.GetProperty("alreadyReported").GetBoolean());
        Assert.False(reportData.TryGetProperty("case", out _));
        Assert.DoesNotContain("caseRef", reportData.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("testimonyCount", reportData.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("escalatedToParticipantRef", reportData.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(postRef, reportBody.GetProperty("receipt").GetProperty("resultRef").GetString());

        using var reportReplay = await app.Http.GetAsync("/api/v1/experience/operations/http-report");
        Assert.Equal(HttpStatusCode.OK, reportReplay.StatusCode);
        var replayText = await reportReplay.Content.ReadAsStringAsync();
        Assert.DoesNotContain("caseRef", replayText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("testimonyCount", replayText, StringComparison.OrdinalIgnoreCase);

        var assigned = await Rooms.SetMembership(app.OwnerParticipantId, Topic,
            ExperienceWebApp.AgentDid, RoomRole.Manager, CancellationToken.None);
        Assert.True(assigned.Accepted, assigned.Reason);
        using var stewardTopic = await app.Http.GetAsync($"/api/v1/experience/topics/{Topic}");
        using (var json = JsonDocument.Parse(await stewardTopic.Content.ReadAsStringAsync()))
            Assert.True(json.RootElement.GetProperty("capabilities").GetProperty("stewardship").GetBoolean());

        using var cases = await app.Http.GetAsync($"/api/v1/experience/topics/{Topic}/moderation/cases");
        Assert.Equal(HttpStatusCode.OK, cases.StatusCode);
        using var casesJson = JsonDocument.Parse(await cases.Content.ReadAsStringAsync());
        var caseRef = casesJson.RootElement.GetProperty("result").GetProperty("data")
            .GetProperty("cases").EnumerateArray().First().GetProperty("caseRef").GetString()!;
        var caseId = caseRef[(caseRef.LastIndexOf("::case_", StringComparison.Ordinal) + 7)..];
        using var detail = await app.Http.GetAsync($"/api/v1/experience/moderation/cases/{caseId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        var summary = detailJson.RootElement.GetProperty("result").GetProperty("data").GetProperty("case");
        var revision = summary.GetProperty("revision").GetInt64();
        var subjectRevision = summary.GetProperty("subjectRevision").GetString();
        var reviewAfter = DateTimeOffset.UtcNow.AddHours(1);
        var action = new
        {
            requestId = "http-defer", action = "defer", summary = "Wait for another account.",
            deferredUntil = reviewAfter, expectedCaseRevision = revision, expectedSubjectRevision = subjectRevision
        };
        using var applied = await app.Http.PostAsJsonAsync($"/api/v1/experience/moderation/cases/{caseId}/actions", action);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);

        var demoted = await Rooms.SetMembership(app.OwnerParticipantId, Topic,
            ExperienceWebApp.AgentDid, RoomRole.Reader, CancellationToken.None);
        Assert.True(demoted.Accepted, demoted.Reason);
        using var deniedReplay = await app.Http.PostAsJsonAsync($"/api/v1/experience/moderation/cases/{caseId}/actions", action);
        Assert.Equal(HttpStatusCode.Forbidden, deniedReplay.StatusCode);
        using var deniedReceipt = await app.Http.GetAsync("/api/v1/experience/operations/http-defer");
        Assert.Equal(HttpStatusCode.Forbidden, deniedReceipt.StatusCode);

        using var owner = Client(app.OwnerToken);
        using var ownerTopic = await owner.GetAsync($"/api/v1/experience/topics/{Topic}");
        using var ownerJson = JsonDocument.Parse(await ownerTopic.Content.ReadAsStringAsync());
        Assert.True(ownerJson.RootElement.GetProperty("capabilities").GetProperty("stewardship").GetBoolean());
    }

    private HttpClient Client(string token)
    {
        var client = new HttpClient { BaseAddress = new Uri(app.Origin) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
