using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Identity;
using Tangent.Community;
using Tangent.Application;
using Tangent.Stewardship;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class ParticipantModerationWorkflowTests
{
    [Fact]
    public async Task Case_actions_require_current_grants_and_real_matching_evidence()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var roles = app.Services.GetRequiredService<RoleCollection>();
        var moderation = app.Services.GetRequiredService<ModerationCaseService>();
        var references = app.Services.GetRequiredService<References>();
        var reported = await moderation.Report(app.OwnerParticipantId, ExperienceWebApp.TopicKey, "m-leo-r1",
            "case-review", "conduct", "Review this post.", ct);
        var caseRef = reported.Case.CaseRef;
        var caseId = references.ParseCase(caseRef)!.Value.CaseId;
        var (credential, token) = ParticipantCredential.Issue(app.AgentParticipantId, "Case steward", 1,
            [ParticipationGrants.Read, ParticipationGrants.Manage], DateTimeOffset.UtcNow);
        await credential.Save(ct);
        app.Http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        await roles.Define("role:case-steward", "Case steward", [TangentPermissions.ManageTopics, TangentPermissions.TimeoutUsers, TangentPermissions.BanUsers], ct: ct);
        await roles.Add("role:case-steward", app.AgentParticipantId, ct);
        var detail = await app.Http.GetFromJsonAsync<JsonElement>("/api/v1/experience/moderation/cases/" + caseId, ct);
        Assert.Equal(app.HumanParticipantId, detail.GetProperty("participantActions").GetProperty("participantRef").GetString());
        Assert.Equal(new[] { "timeout", "ban" }, detail.GetProperty("participantActions").GetProperty("actions").EnumerateArray().Select(value => value.GetString()).ToArray());
        var route = "/api/v1/experience/moderation/users/" + app.HumanParticipantId + "/management";
        var madeUp = references.Case(ExperienceWebApp.TangentKey, ExperienceWebApp.TopicKey, new string('f', 64));
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Http.PostAsJsonAsync(route,
            new { action = "timeout", reason = "No evidence.", durationSeconds = 3600, caseRef = madeUp }, ct)).StatusCode);
        (await app.Http.PostAsJsonAsync(route, new { action = "timeout", reason = "A cooling off period.", durationSeconds = 3600, caseRef }, ct)).EnsureSuccessStatusCode();
        using (EntityContext.NoCache()) Assert.Equal(RestrictionKind.Timeout,
            (await ScopedRestriction.Get(ScopedRestriction.Key(RestrictionScope.Tangent, ExperienceWebApp.TangentKey, app.HumanParticipantId), ct))!.Kind);
        await roles.Remove("role:case-steward", app.AgentParticipantId, ct);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Http.PostAsJsonAsync(route,
            new { action = "ban", reason = "Grant revoked.", caseRef }, ct)).StatusCode);
    }

    [Fact]
    public async Task Delegated_people_stewards_can_suspend_restore_and_lose_actions_when_revoked()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var roles = app.Services.GetRequiredService<RoleCollection>();
        const string role = "role:people-steward";
        await roles.Define(role, "People steward", [TangentPermissions.ManageParticipants], ct: ct);
        await roles.Add(role, app.AgentParticipantId, ct);
        var (credential, token) = ParticipantCredential.Issue(app.AgentParticipantId, "People steward", 1,
            [ParticipationGrants.Read, ParticipationGrants.Manage], DateTimeOffset.UtcNow);
        await credential.Save(ct);
        app.Http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var endpoint = "/api/people/" + Uri.EscapeDataString(ExperienceWebApp.HumanHandle) + "/moderation";

        Assert.Contains("suspend", await Actions(endpoint));
        (await app.Http.PostAsJsonAsync(endpoint, new { action = "suspend", note = "Review account activity." }, ct)).EnsureSuccessStatusCode();
        using (EntityContext.NoCache()) Assert.True((await Participant.Get(app.HumanParticipantId, ct))!.IsSuspended);
        Assert.Contains("restore", await Actions(endpoint));
        (await app.Http.PostAsJsonAsync(endpoint, new { action = "restore", note = "Review completed." }, ct)).EnsureSuccessStatusCode();
        using (EntityContext.NoCache()) Assert.False((await Participant.Get(app.HumanParticipantId, ct))!.IsSuspended);

        foreach (var identifier in new[] { ExperienceWebApp.AgentDid, app.OwnerParticipantId })
        {
            var protectedEndpoint = "/api/people/" + Uri.EscapeDataString(identifier) + "/moderation";
            Assert.Empty(await Actions(protectedEndpoint));
            Assert.Equal(HttpStatusCode.Forbidden, (await app.Http.PostAsJsonAsync(protectedEndpoint,
                new { action = "suspend", note = "Protected target." }, ct)).StatusCode);
        }
        await roles.Remove(role, app.AgentParticipantId, ct);
        Assert.DoesNotContain("suspend", await Actions(endpoint));
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Http.PostAsJsonAsync(endpoint,
            new { action = "suspend", note = "Grant has been revoked." }, ct)).StatusCode);

        async Task<string[]> Actions(string route)
        {
            var response = await app.Http.GetFromJsonAsync<JsonElement>(route, ct);
            return response.GetProperty("actions").EnumerateArray().Select(value => value.GetString()!).ToArray();
        }
    }
}
