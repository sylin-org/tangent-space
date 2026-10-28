using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

// Shares the serialized collection with every other integration class: these fixtures stand up
// a real host over shared state, and two of them at once corrupt each other's transactions.
[Xunit.Collection("Experience integration")]
public sealed class AccessSettingsTests
{
    [Fact]
    public async Task Topic_settings_patch_preserves_fields_owned_by_other_sections()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);
        var settingsPath = $"/api/v1/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}/settings";
        var topicPath = $"/api/v1/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}";

        using (var participation = await app.Http.PatchAsync(settingsPath,
            JsonContent.Create(new { allowPostEditing = true, isLocked = true }), TestContext.Current.CancellationToken))
            participation.EnsureSuccessStatusCode();
        using (var details = await app.Http.PatchAsync(settingsPath,
            JsonContent.Create(new { title = "A revised Topic", topic = "A revised description" }), TestContext.Current.CancellationToken))
            details.EnsureSuccessStatusCode();
        using (var read = await app.Http.GetAsync(topicPath, TestContext.Current.CancellationToken))
        {
            read.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("A revised Topic", document.RootElement.GetProperty("title").GetString());
            Assert.Equal("A revised description", document.RootElement.GetProperty("topic").GetString());
            Assert.True(document.RootElement.GetProperty("allowPostEditing").GetBoolean());
            Assert.True(document.RootElement.GetProperty("isLocked").GetBoolean());
        }

        using (var participation = await app.Http.PatchAsync(settingsPath,
            JsonContent.Create(new { isLocked = false }), TestContext.Current.CancellationToken))
            participation.EnsureSuccessStatusCode();
        using (var read = await app.Http.GetAsync(topicPath, TestContext.Current.CancellationToken))
        {
            read.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await read.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("A revised Topic", document.RootElement.GetProperty("title").GetString());
            Assert.Equal("A revised description", document.RootElement.GetProperty("topic").GetString());
            Assert.True(document.RootElement.GetProperty("allowPostEditing").GetBoolean());
            Assert.False(document.RootElement.GetProperty("isLocked").GetBoolean());
        }
    }

    [Fact]
    public async Task Owner_can_set_then_restore_a_topic_access_map_without_policy_versions()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);

        var path = $"/api/v1/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}/access";
        using var replace = await app.Http.PutAsJsonAsync(path,
            new { access = new { see = new[] { "role:secret_club" }, post = new[] { "role:secret_club" }, manage = Array.Empty<string>() }, publishExistingHistory = false },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        using (var document = JsonDocument.Parse(await replace.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
        {
            Assert.Equal("role:secret_club", document.RootElement.GetProperty("selected").GetProperty("see")[0].GetString());
            Assert.Contains(document.RootElement.GetProperty("effective").GetProperty("see").EnumerateArray(),
                value => value.GetString() == "global:topic_read");
        }

        using var reset = await app.Http.PutAsJsonAsync(path,
            new { access = new { see = (string[]?)null, post = (string[]?)null, manage = (string[]?)null }, publishExistingHistory = false },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        using var resetDocument = JsonDocument.Parse(await reset.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("see", resetDocument.RootElement.GetProperty("inherited").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains(resetDocument.RootElement.GetProperty("parent").GetProperty("see").EnumerateArray(),
            value => value.GetString() == "role:member");
        Assert.Contains(resetDocument.RootElement.GetProperty("effective").GetProperty("see").EnumerateArray(),
            value => value.GetString() == "role:member");
    }

    [Fact]
    public async Task Owner_can_manage_the_single_server_role_collection_and_its_members()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);

        using var page = await app.Http.GetAsync("/api/identity/roles?page=1&pageSize=100",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using (var pageDocument = JsonDocument.Parse(await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
        {
            // The Owner role holds no members: the owner's authority derives from the Space
            // (ADR 0013, N-025), so a membership row recording it would be a second truth.
            var owner = pageDocument.RootElement.GetProperty("items").EnumerateArray()
                .Single(role => role.GetProperty("id").GetString() == "role:owner");
            Assert.Empty(owner.GetProperty("members").EnumerateArray());
        }
        // The owner's current management authority is visible without a stored Owner membership.
        using (var arrival = await app.Http.GetAsync("/api/v1/experience", TestContext.Current.CancellationToken))
        {
            arrival.EnsureSuccessStatusCode();
            using var arrivalDocument = JsonDocument.Parse(await arrival.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var authority = arrivalDocument.RootElement.GetProperty("authority");
            Assert.True(authority.GetProperty("canManage").GetBoolean());
            var management = authority.GetProperty("management");
            foreach (var fact in new[] { "canWarn", "canTimeout", "canSuspend", "canBan", "canAssignRoles" })
                Assert.True(management.GetProperty(fact).GetBoolean());
        }

        const string rolePath = "/api/identity/roles/role:test-gardeners";
        using var create = await app.Http.PutAsJsonAsync(rolePath, new
        {
            name = "Gardeners",
            permissions = new[] { "global:post_create" },
            metadata = new Dictionary<string, string> { ["purpose"] = "Tend the test garden.", ["color"] = "#6fd9b3" }
        }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var add = await app.Http.PutAsync($"{rolePath}/members/{app.HumanParticipantId}", null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        using (var memberDocument = JsonDocument.Parse(await add.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
            Assert.Contains(memberDocument.RootElement.GetProperty("members").EnumerateArray(),
                member => member.GetString() == app.HumanParticipantId);

        using var remove = await app.Http.DeleteAsync($"{rolePath}/members/{app.HumanParticipantId}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        using (var memberDocument = JsonDocument.Parse(await remove.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
            Assert.DoesNotContain(memberDocument.RootElement.GetProperty("members").EnumerateArray(),
                member => member.GetString() == app.HumanParticipantId);
    }

    [Fact]
    public async Task Role_editor_support_endpoints_require_a_browser_cookie()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);

        using var descriptor = await app.Http.GetAsync("/api/roles/ui/descriptor",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, descriptor.StatusCode);

        using var session = await app.Http.GetAsync("/api/roles/ui/session",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }
}
