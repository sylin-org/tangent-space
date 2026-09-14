using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TangentSpace.Tests.ExperienceIntegration;

public sealed class AccessSettingsTests
{
    [Fact]
    public async Task Owner_can_replace_then_restore_a_topic_policy_through_the_real_Koan_endpoint()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);

        var scope = $"/api/identity/scoped-roles/tangent-space/topic/{ExperienceWebApp.TopicKey}";
        using var replace = await app.Http.PutAsJsonAsync(scope + "/policies/topic.read",
            new { audience = new[] { new { kind = 1 } } }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        Assert.NotNull(replace.Headers.ETag);
        using (var document = JsonDocument.Parse(await replace.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)))
            Assert.Equal("topic.read", document.RootElement.GetProperty("capability").GetString());

        using var resetRequest = new HttpRequestMessage(HttpMethod.Delete, scope + "/policies/topic.read");
        resetRequest.Headers.IfMatch.Add(replace.Headers.ETag!);
        using var reset = await app.Http.SendAsync(resetRequest, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
    }
}
