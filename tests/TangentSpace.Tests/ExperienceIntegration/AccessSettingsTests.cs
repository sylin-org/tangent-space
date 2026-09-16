using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

public sealed class AccessSettingsTests
{
    [Fact]
    public async Task Owner_can_set_then_restore_a_topic_access_map_without_policy_versions()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);

        var path = $"/api/v1/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}/access";
        using var replace = await app.Http.PutAsJsonAsync(path,
            new { see = new[] { "role:secret_club" }, post = new[] { "role:secret_club" }, manage = Array.Empty<string>() },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        using (var document = JsonDocument.Parse(await replace.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
        {
            Assert.Equal("role:secret_club", document.RootElement.GetProperty("selected").GetProperty("see")[0].GetString());
            Assert.Contains(document.RootElement.GetProperty("effective").GetProperty("see").EnumerateArray(),
                value => value.GetString() == "global:topic_read");
        }

        using var reset = await app.Http.PutAsJsonAsync(path,
            new { see = (string[]?)null, post = (string[]?)null, manage = (string[]?)null },
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
            var owner = pageDocument.RootElement.GetProperty("items").EnumerateArray()
                .Single(role => role.GetProperty("id").GetString() == "role:owner");
            Assert.Contains(owner.GetProperty("members").EnumerateArray(),
                member => member.GetString() == app.OwnerParticipantId);
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
    public async Task Role_editor_support_endpoints_are_available_in_the_real_application_host()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);

        using var descriptor = await app.Http.GetAsync("/api/roles/ui/descriptor",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, descriptor.StatusCode);
        using (var document = JsonDocument.Parse(await descriptor.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
            Assert.NotEmpty(document.RootElement.GetProperty("capabilities").EnumerateArray());

        using var session = await app.Http.GetAsync("/api/roles/ui/session",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using (var document = JsonDocument.Parse(await session.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
        {
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("requestToken").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("headerName").GetString()));
        }
    }
}
