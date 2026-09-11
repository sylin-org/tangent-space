using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace TangentSpace.Tests.ExperienceIntegration;

/// <summary>Facets, mentionables, groups and the profile page — the ADR 0008 layer against
/// the real web application. Facet mentions are trusted structure (a typo'd label with a
/// correct DID still directs attention), groups expand at digest time to current holders,
/// and the profile serves identity, roles, policy-scoped posts and viewer actions.</summary>
[Xunit.Collection("Experience integration")]
public sealed class FacetIntegrationTests : IAsyncLifetime
{
    private ExperienceWebApp app = null!;

    public async ValueTask InitializeAsync()
    {
        app = await ExperienceWebApp.StartAsync();
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();

    private async Task<JsonElement> Post(string topicKey, object body)
    {
        using var response = await app.Http.PostAsJsonAsync($"/api/v1/experience/topics/{topicKey}/posts", body);
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task Facets_round_trip_with_a_fresh_resolution_map()
    {
        // The picker-minted facet binds the DID even though the visible label is a typo:
        // storage keeps the verbatim text, reads return structure plus fresh labels.
        var text = "Ping @agnent-experience — please review.";
        var start = System.Text.Encoding.UTF8.GetByteCount("Ping ");
        var end = start + System.Text.Encoding.UTF8.GetByteCount("@agnent-experience");
        var created = await Post(ExperienceWebApp.TopicKey, new
        {
            requestId = "facet-roundtrip-1",
            text,
            facets = new[] { new { kind = "mention", start, end, did = ExperienceWebApp.AgentDid } },
        });
        Assert.Equal("completed", created.GetProperty("result").GetProperty("receipt").GetProperty("state").GetString());

        using var read = await app.Http.GetAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}?limit=10");
        var document = JsonDocument.Parse(await read.Content.ReadAsStringAsync()).RootElement.Clone();
        var posts = document.GetProperty("result").GetProperty("data").GetProperty("posts");
        var posted = Enumerable.Range(0, posts.GetArrayLength()).Select(index => posts[index])
            .Single(post => post.GetProperty("text").GetString() == text);
        var facets = posted.GetProperty("facets");
        Assert.Equal(1, facets.GetArrayLength());
        Assert.Equal("mention", facets[0].GetProperty("kind").GetString());
        Assert.Equal(start, facets[0].GetProperty("start").GetInt32());
        Assert.Equal(ExperienceWebApp.AgentDid, facets[0].GetProperty("did").GetString());
        var resolved = document.GetProperty("result").GetProperty("data").GetProperty("resolved");
        Assert.Equal(ExperienceWebApp.AgentHandle, resolved.GetProperty(ExperienceWebApp.AgentDid).GetProperty("handle").GetString());
    }

    [Fact]
    public async Task A_facet_mention_directs_attention_even_with_a_misspelled_label()
    {
        // Trusted structure: the prose never names the agent correctly, the facet does.
        var text = "Hey @agnent-experience, ready?";
        var start = 4;
        var end = start + System.Text.Encoding.UTF8.GetByteCount("@agnent-experience");
        await Post(ExperienceWebApp.TopicKey, new
        {
            requestId = "facet-attention-1",
            text,
            facets = new[] { new { kind = "mention", start, end, did = ExperienceWebApp.HumanDid == ExperienceWebApp.AgentDid ? "" : ExperienceWebApp.AgentDid } },
        });

        using var updates = await app.Http.GetAsync("/api/v1/experience/updates");
        var document = JsonDocument.Parse(await updates.Content.ReadAsStringAsync()).RootElement.Clone();
        var attention = document.GetProperty("attention");
        Assert.Contains(attention.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("relationship").GetString() == "addressed_to_you"
                && item.GetProperty("kind").GetString() == "direct_mention");
    }

    [Fact]
    public async Task A_group_mention_directs_attention_to_current_holders()
    {
        // @admins expands at digest time: the owner (server owner) holds the group, so the
        // owner's digest shows the agent's group mention as a directed request.
        var text = "@admins the build is green; please take a look.";
        var start = 0;
        var end = System.Text.Encoding.UTF8.GetByteCount("@admins");
        await Post(ExperienceWebApp.TopicKey, new
        {
            requestId = "facet-group-1",
            text,
            facets = new[] { new { kind = "group", start, end, value = "admins" } },
        });

        using var owner = new HttpClient { BaseAddress = new Uri(app.Origin) };
        owner.DefaultRequestHeaders.Authorization = new("Bearer", app.OwnerToken);
        using var updates = await owner.GetAsync("/api/v1/experience/updates");
        var document = JsonDocument.Parse(await updates.Content.ReadAsStringAsync()).RootElement.Clone();
        var items = document.GetProperty("attention").GetProperty("items");
        Assert.Contains(items.EnumerateArray(), item =>
            item.GetProperty("relationship").GetString() == "addressed_to_you"
            && item.GetProperty("kind").GetString() == "direct_mention");
    }

    [Fact]
    public async Task Mentionables_ranks_participants_and_offers_role_groups()
    {
        // The viewer (agent) is deliberately absent from their own candidates; the human
        // author of the seeded history is present and prefix-ranked.
        using var response = await app.Http.GetAsync(
            $"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/mentionables?prefix=le");
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        var targets = document.GetProperty("result").GetProperty("data").GetProperty("targets");
        Assert.Contains(targets.EnumerateArray(), target =>
            target.GetProperty("kind").GetString() == "participant"
            && target.GetProperty("did").GetString() == ExperienceWebApp.HumanDid);
        Assert.DoesNotContain(targets.EnumerateArray(), target => target.GetProperty("did").GetString() == ExperienceWebApp.AgentDid);

        using var groups = await app.Http.GetAsync(
            $"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/mentionables?prefix=ad");
        var groupDocument = JsonDocument.Parse(await groups.Content.ReadAsStringAsync()).RootElement.Clone();
        var groupTargets = groupDocument.GetProperty("result").GetProperty("data").GetProperty("targets");
        var admins = Enumerable.Range(0, groupTargets.GetArrayLength()).Select(index => groupTargets[index])
            .Where(target => target.GetProperty("kind").GetString() == "group").ToList();
        Assert.Contains(admins, group => group.GetProperty("label").GetString() == "Admins");
        Assert.True(admins.All(group => group.TryGetProperty("count", out var count) && count.GetInt32() >= 1));
    }

    [Fact]
    public async Task Invalid_facets_are_rejected_honestly()
    {
        var body = new
        {
            requestId = "facet-invalid-1",
            text = "Range outside the text.",
            facets = new[] { new { kind = "mention", start = 0, end = 9999, did = ExperienceWebApp.AgentDid } },
        };
        using var response = await app.Http.PostAsJsonAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/posts", body);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_profile_serves_identity_roles_posts_and_viewer_actions()
    {
        using var response = await app.Http.GetAsync($"/api/participants/{ExperienceWebApp.AgentDid}/profile");
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        var data = document.GetProperty("result").GetProperty("data");
        Assert.Equal(ExperienceWebApp.AgentHandle, data.GetProperty("handle").GetString());
        Assert.True(data.GetProperty("self").GetBoolean());
        var roles = data.GetProperty("roles");
        Assert.Contains(roles.EnumerateArray(), role => role.GetProperty("scope").GetString() == "tangent"
            && role.GetProperty("role").GetString() == "member");
        var posts = data.GetProperty("posts");
        Assert.Contains(posts.EnumerateArray(), post => post.GetProperty("text").GetString()!.Contains("review the Project Z plan"));
        var actions = document.GetProperty("actions");
        Assert.Contains(actions.EnumerateArray(), action => action.GetProperty("name").GetString() == "declare_self");
    }

    [Fact]
    public async Task A_facet_mention_with_a_changed_payload_conflicts()
    {
        var text = "Same key, different facets.";
        await Post(ExperienceWebApp.TopicKey, new
        {
            requestId = "facet-conflict-1",
            text,
            facets = new[] { new { kind = "mention", start = 0, end = 4, did = ExperienceWebApp.HumanDid } },
        });
        var body = new
        {
            requestId = "facet-conflict-1",
            text,
            facets = new[] { new { kind = "mention", start = 0, end = 4, did = ExperienceWebApp.AgentDid } },
        };
        using var response = await app.Http.PostAsJsonAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/posts", body);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
    }
}
