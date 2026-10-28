using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Community;
using Tangent.Identity;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class TangentMemberDirectoryTests
{
    [Fact]
    public async Task Members_are_scoped_paged_and_exact_preview_shows_current_standing()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);
        var path = $"/api/v1/tangents/{ExperienceWebApp.TangentKey}";
        using (var first = await app.Http.GetAsync(path + "/members", ct))
        {
            first.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await first.Content.ReadAsStringAsync(ct));
            var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Contains(items, item => item.GetProperty("participantId").GetString() == app.OwnerParticipantId
                && item.GetProperty("role").GetString() == "Owner");
            Assert.Contains(items, item => item.GetProperty("participantId").GetString() == app.AgentParticipantId
                && item.GetProperty("role").GetString() == "Member");
            Assert.DoesNotContain(items, item => item.GetProperty("participantId").GetString() == app.HumanParticipantId);
        }
        using (var lookup = await app.Http.GetAsync(path + "/membership-preview?identifier=%40LEO.EXPERIENCE.TEST", ct))
        {
            lookup.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await lookup.Content.ReadAsStringAsync(ct));
            Assert.Equal(app.HumanParticipantId, document.RootElement.GetProperty("participantId").GetString());
            Assert.Equal("None", document.RootElement.GetProperty("role").GetString());
            Assert.True(document.RootElement.GetProperty("canChangeRole").GetBoolean());
        }
        using (var unknown = await app.Http.GetAsync(path + "/membership-preview?identifier=missing.experience.test", ct))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.AgentToken);
        using (var denied = await app.Http.GetAsync(path + "/members", ct))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);

        var tangent = (await Community.Tangent.Get(ExperienceWebApp.TangentKey, ct))!;
        var other = await app.Services.GetRequiredService<TangentGovernance>().Create(app.OwnerParticipantId,
            "other-members", "Other members", "Separate community", null, null, null, ct);
        var expected = new HashSet<string>(StringComparer.Ordinal) { app.AgentParticipantId };
        for (var n = 0; n < 35; n++)
        {
            var participant = new Participant { Id = Participant.NewIdentifier() };
            await participant.Save(ct);
            await ParticipantIdentity.Internal(participant.Id, DateTimeOffset.UtcNow).Save(ct);
            var assigned = n == 34 ? other.Key : tangent.Id;
            await TangentMembership.Assign(assigned == tangent.Id ? tangent : (await Community.Tangent.Get(other.Key, ct))!,
                participant.Id, TangentRole.Reader, app.OwnerParticipantId, DateTimeOffset.UtcNow).Save(ct);
            if (assigned == tangent.Id) expected.Add(participant.Id);
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            using var response = await app.Http.GetAsync(path + "/members" + (cursor is null ? "" : "?cursor=" + cursor), ct);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.InRange(items.Length, 1, TangentMemberDirectory.PageSize + 1);
            foreach (var item in items) Assert.True(seen.Add(item.GetProperty("participantId").GetString()!));
            cursor = document.RootElement.TryGetProperty("nextCursor", out var continuation)
                ? continuation.GetString() : null;
        } while (cursor is not null);
        expected.Add(app.OwnerParticipantId);
        Assert.Equal(expected.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Scoped_administrator_can_manage_lower_roles_but_cannot_grant_administrator_or_edit_admission()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var governance = app.Services.GetRequiredService<ParticipantGovernance>();
        await governance.SetRole(app.OwnerParticipantId, ExperienceWebApp.TangentKey, null,
            ExperienceWebApp.AgentDid, ParticipantRole.Admin, ct);
        var (credential, token) = ParticipantCredential.Issue(app.AgentParticipantId,
            "Tangent administrator test", 1,
            [ParticipationGrants.Read, ParticipationGrants.Manage], DateTimeOffset.UtcNow);
        await credential.Save(ct);
        using var admin = new HttpClient { BaseAddress = new Uri(app.Origin) };
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var path = $"/api/v1/tangents/{ExperienceWebApp.TangentKey}";
        using (var description = await admin.GetAsync(path, ct))
        {
            description.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await description.Content.ReadAsStringAsync(ct));
            Assert.True(document.RootElement.GetProperty("canManage").GetBoolean());
        }
        using (var preview = await admin.GetAsync(path + "/membership-preview?identifier=" + ExperienceWebApp.HumanDid, ct))
        {
            preview.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await preview.Content.ReadAsStringAsync(ct));
            Assert.Equal(new[] { "Member", "Reader" }, document.RootElement.GetProperty("allowedRoles")
                .EnumerateArray().Select(value => value.GetString()).ToArray());
        }
        var write = $"/api/tangents/{ExperienceWebApp.TangentKey}/roles/{ExperienceWebApp.HumanDid}";
        using (var member = await admin.PutAsJsonAsync(write, new { role = "Member" }, ct))
            Assert.Equal(HttpStatusCode.OK, member.StatusCode);
        using (var adminGrant = await admin.PutAsJsonAsync(write, new { role = "Admin" }, ct))
            Assert.Equal(HttpStatusCode.Forbidden, adminGrant.StatusCode);
        using (var admission = await admin.PutAsJsonAsync(path + "/admission", new { admission = "Approval" }, ct))
            Assert.Equal(HttpStatusCode.Forbidden, admission.StatusCode);
    }

    [Fact]
    public async Task Owner_changes_admission_without_changing_participation_classification()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);
        var path = $"/api/v1/tangents/{ExperienceWebApp.TangentKey}/admission";
        using var change = await app.Http.PutAsJsonAsync(path, new { admission = "Approval" }, ct);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        using var document = JsonDocument.Parse(await change.Content.ReadAsStringAsync(ct));
        Assert.Equal("Approval", document.RootElement.GetProperty("admission").GetString());
        Assert.Equal("Everyone", document.RootElement.GetProperty("preset").GetString());
        Assert.Equal("Write", document.RootElement.GetProperty("undeclared").GetString());
        Assert.Equal(TangentAdmission.Approval, (await Community.Tangent.Get(ExperienceWebApp.TangentKey, ct))!.EffectiveAdmission);
    }
}
