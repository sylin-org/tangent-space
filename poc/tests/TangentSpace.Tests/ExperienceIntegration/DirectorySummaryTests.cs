using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Community;
using Tangent.Conversation;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class DirectorySummaryTests
{
    [Fact]
    public async Task Markdown_latest_contribution_uses_visible_words_in_public_directories()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var markdown = "# Kintsugi Architecture\n\nA [careful repair](https://example.org/essay) " +
            "keeps the structure **visible**.\n\n![Diagram](/media/0123456789abcdef0123456789abcdef)";
        Assert.Equal("[Plain](literal)", PostMarkup.PlainText(
            new PostContent("[Plain](literal)", default, null)));
        var post = new Post
        {
            Id = "m-markdown-summary", TopicKey = ExperienceWebApp.TopicKey,
            AuthorParticipantId = app.OwnerParticipantId, Sequence = 4,
            AcceptedAt = DateTimeOffset.UtcNow,
            Content = new PostContent(markdown, DateTimeOffset.UtcNow, null, PostContent.Markdown)
        };
        await post.Save(ct);
        await app.Services.GetRequiredService<TangentGovernance>().SetAccess(app.OwnerParticipantId,
            ExperienceWebApp.TangentKey, new AccessMap { See = [AccessTokens.Everyone] }, ct);
        Assert.True((await app.Services.GetRequiredService<TopicGovernance>().SetReadAudience(app.OwnerParticipantId,
            ExperienceWebApp.TopicKey, TopicReadAudience.Public, true, ct)).Accepted);
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        foreach (var path in new[] { "/api/v1/tangents/proj", "/api/v1/tangents/proj/topics/proj-z" })
        {
            using var response = await anonymous.GetAsync(path, ct);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var excerpt = document.RootElement.GetProperty("summary").GetProperty("latestPost")
                .GetProperty("excerpt").GetString()!;
            Assert.StartsWith("Kintsugi Architecture A careful repair keeps the structure visible.", excerpt);
            Assert.DoesNotContain("https://", excerpt);
            Assert.DoesNotContain("/media/", excerpt);
            Assert.DoesNotContain("![", excerpt);
            Assert.DoesNotContain("](", excerpt);
            Assert.DoesNotContain("**", excerpt);
            Assert.True(excerpt.Length <= 161);
        }
    }

    [Fact]
    public async Task Anonymous_summary_counts_only_readable_live_posts_and_never_previews_private_topics()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var tangents = app.Services.GetRequiredService<TangentGovernance>();
        var topics = app.Services.GetRequiredService<TopicGovernance>();
        await tangents.SetAccess(app.OwnerParticipantId, ExperienceWebApp.TangentKey,
            new AccessMap { See = [AccessTokens.Everyone], Post = ["role:member"] }, ct);
        Assert.True((await topics.SetReadAudience(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            TopicReadAudience.Public, true, ct)).Accepted);
        await tangents.CreateTopic(app.OwnerParticipantId, ExperienceWebApp.TangentKey,
            "private-summary", "Secret title", "Secret content", ct);
        await ExperienceWebApp.Accept("private-summary", app.OwnerParticipantId, "private-summary", 1,
            DateTimeOffset.UtcNow.AddMinutes(5), "A private post", null);
        var removed = (await Post.Get("m-leo-m1", ct))!;
        removed.Removed = true;
        await removed.Save(ct);
        var template = (await Topic.Get(ExperienceWebApp.TopicKey, ct))!;
        for (var index = 0; index < 101; index++)
        {
            await new Topic
            {
                Id = $"hidden-{index:000}", TangentKey = template.TangentKey, Title = "Hidden title",
                CreatorParticipantId = template.CreatorParticipantId, CreatedAt = template.CreatedAt,
                UpdatedAt = template.UpdatedAt, PolicyRevision = 1,
                ReadAudience = TopicReadAudience.Public, Access = new AccessMap { See = ["role:member"] }
            }.Save(ct);
        }
        await new Post
        {
            Id = "private-history-snapshot", TopicKey = template.Id, AuthorParticipantId = app.OwnerParticipantId,
            Sequence = 1000, AcceptedAt = DateTimeOffset.UtcNow, OfPostId = removed.Id,
            Content = new PostContent("Secret pre-edit text", DateTimeOffset.UtcNow, null)
        }.Save(ct);

        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        using var response = await anonymous.GetAsync("/api/v1/tangents/proj", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(ct);
        using var document = JsonDocument.Parse(body);
        var summary = document.RootElement.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("topics").GetProperty("value").GetInt64());
        Assert.Equal(2, summary.GetProperty("posts").GetProperty("value").GetInt64());
        Assert.False(summary.GetProperty("incomplete").GetBoolean());
        Assert.Equal("m-leo-r1", summary.GetProperty("latestPost").GetProperty("id").GetString());
        Assert.DoesNotContain("Secret", body);

        using var directoryResponse = await anonymous.GetAsync("/api/v1/tangents", ct);
        directoryResponse.EnsureSuccessStatusCode();
        using var directoryDocument = JsonDocument.Parse(await directoryResponse.Content.ReadAsStringAsync(ct));
        var card = directoryDocument.RootElement.GetProperty("tangents").EnumerateArray()
            .Single(item => item.GetProperty("key").GetString() == ExperienceWebApp.TangentKey);
        Assert.Equal(1, card.GetProperty("summary").GetProperty("topics").GetProperty("value").GetInt64());

        using var topicsResponse = await anonymous.GetAsync("/api/v1/tangents/proj/topics", ct);
        topicsResponse.EnsureSuccessStatusCode();
        using var topicsDocument = JsonDocument.Parse(await topicsResponse.Content.ReadAsStringAsync(ct));
        var listed = topicsDocument.RootElement.GetProperty("topics").EnumerateArray().ToArray();
        Assert.Single(listed);
        Assert.Equal("proj-z", listed[0].GetProperty("key").GetString());
        Assert.Equal(2, listed[0].GetProperty("summary").GetProperty("posts").GetProperty("value").GetInt64());
        Assert.False(topicsDocument.RootElement.TryGetProperty("nextPage", out var nextPage)
            && nextPage.ValueKind != JsonValueKind.Null);
        Assert.False(listed[0].GetProperty("canWrite").GetBoolean());
        Assert.False(listed[0].GetProperty("canManage").GetBoolean());
        Assert.True((await topics.ReadPolicyAsync(null, ExperienceWebApp.TopicKey, ct)).CanRead);

        using var allTopicsResponse = await anonymous.GetAsync("/api/topics", ct);
        allTopicsResponse.EnsureSuccessStatusCode();
        using var allTopicsDocument = JsonDocument.Parse(await allTopicsResponse.Content.ReadAsStringAsync(ct));
        Assert.Single(allTopicsDocument.RootElement.GetProperty("topics").EnumerateArray());
        Assert.False(allTopicsDocument.RootElement.TryGetProperty("nextPage", out var allNextPage)
            && allNextPage.ValueKind != JsonValueKind.Null);

        using var topicResponse = await anonymous.GetAsync("/api/v1/tangents/proj/topics/proj-z", ct);
        topicResponse.EnsureSuccessStatusCode();
        using var topicDocument = JsonDocument.Parse(await topicResponse.Content.ReadAsStringAsync(ct));
        var topicSummary = topicDocument.RootElement.GetProperty("summary");
        Assert.Equal(2, topicSummary.GetProperty("posts").GetProperty("value").GetInt64());
        Assert.Equal(2, topicSummary.GetProperty("participants").GetProperty("value").GetInt64());
        Assert.Equal("m-leo-r1", topicSummary.GetProperty("latestPost").GetProperty("id").GetString());
        Assert.Equal(app.OwnerParticipantId, topicSummary.GetProperty("starter").GetProperty("id").GetString());
        Assert.Equal(app.AgentParticipantId, topicSummary.GetProperty("openingPost").GetProperty("author").GetProperty("id").GetString());
        Assert.Equal("m-agent-q1", topicSummary.GetProperty("openingPost").GetProperty("id").GetString());
        Assert.Equal(1, topicSummary.GetProperty("replies").GetProperty("value").GetInt64());
        Assert.True(topicSummary.GetProperty("createdAt").GetDateTimeOffset() > DateTimeOffset.MinValue);

        using var publicPostResponse = await anonymous.GetAsync("/t/proj/m-leo-r1", ct);
        publicPostResponse.EnsureSuccessStatusCode();
        var publicHtml = await publicPostResponse.Content.ReadAsStringAsync(ct);
        Assert.Contains("/public.css?", publicHtml);
        Assert.Contains("Here is the summary: three open questions remain.", publicHtml);
        Assert.DoesNotContain("Secret", publicHtml);

        using var privateResponse = await anonymous.GetAsync("/api/v1/tangents/proj/topics/private-summary", ct);
        Assert.Equal(HttpStatusCode.NotFound, privateResponse.StatusCode);
    }

    [Fact]
    public async Task Manage_only_topics_have_no_post_or_participant_summary()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var tangents = app.Services.GetRequiredService<TangentGovernance>();
        var topics = app.Services.GetRequiredService<TopicGovernance>();
        await tangents.CreateTopic(app.AgentParticipantId, ExperienceWebApp.TangentKey,
            "managed-secret", "Managed topic", "", ct);
        Assert.True((await topics.SetAccess(app.OwnerParticipantId, "managed-secret",
            new AccessMap { See = [] }, ct)).Accepted);
        await ExperienceWebApp.Accept("managed-secret", app.OwnerParticipantId, "managed-secret", 1,
            DateTimeOffset.UtcNow, "Private managed content", null);

        using var response = await app.Http.GetAsync("/api/v1/tangents/proj/topics/managed-secret", ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.True(document.RootElement.GetProperty("canManage").GetBoolean());
        Assert.False(document.RootElement.GetProperty("canRead").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("summary", out var restrictedSummary)
            && restrictedSummary.ValueKind != JsonValueKind.Null);

        using var tangentResponse = await app.Http.GetAsync("/api/v1/tangents/proj", ct);
        tangentResponse.EnsureSuccessStatusCode();
        var body = await tangentResponse.Content.ReadAsStringAsync(ct);
        using var tangentDocument = JsonDocument.Parse(body);
        var summary = tangentDocument.RootElement.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("topics").GetProperty("value").GetInt64());
        Assert.Equal(3, summary.GetProperty("posts").GetProperty("value").GetInt64());
        Assert.DoesNotContain("Private managed content", body);
    }

    [Fact]
    public async Task Tangent_latest_uses_acceptance_time_across_topics_and_participant_sample_is_a_lower_bound()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var tangents = app.Services.GetRequiredService<TangentGovernance>();
        await tangents.CreateTopic(app.OwnerParticipantId, ExperienceWebApp.TangentKey,
            "new-conversation", "New conversation", "", ct);
        await ExperienceWebApp.Accept("new-conversation", app.OwnerParticipantId, "new-conversation", 1,
            DateTimeOffset.UtcNow, "Newest across conversations", null);
        for (var index = 4; index <= 130; index++)
        {
            await new Post
            {
                Id = $"older-{index:000}", TopicKey = ExperienceWebApp.TopicKey,
                AuthorParticipantId = app.HumanParticipantId, Sequence = index,
                AcceptedAt = DateTimeOffset.UtcNow.AddHours(-1),
                Content = new PostContent("Older post", DateTimeOffset.UtcNow.AddHours(-1), null)
            }.Save(ct);
        }
        using var response = await app.Http.GetAsync("/api/v1/tangents/proj", ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var summary = document.RootElement.GetProperty("summary");
        Assert.Equal(131, summary.GetProperty("posts").GetProperty("value").GetInt64());
        Assert.Equal("m-new-conversation", summary.GetProperty("latestPost").GetProperty("id").GetString());

        using var topicResponse = await app.Http.GetAsync("/api/v1/tangents/proj/topics/proj-z", ct);
        topicResponse.EnsureSuccessStatusCode();
        using var topicDocument = JsonDocument.Parse(await topicResponse.Content.ReadAsStringAsync(ct));
        var topicSummary = topicDocument.RootElement.GetProperty("summary");
        Assert.Equal(130, topicSummary.GetProperty("posts").GetProperty("value").GetInt64());
        Assert.False(topicSummary.GetProperty("posts").GetProperty("atLeast").GetBoolean());
        Assert.Equal(1, topicSummary.GetProperty("participants").GetProperty("value").GetInt64());
        Assert.True(topicSummary.GetProperty("participants").GetProperty("atLeast").GetBoolean());
    }

    [Fact]
    public async Task Tangent_summary_marks_counts_as_lower_bounds_when_policy_scan_is_capped()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var template = (await Topic.Get(ExperienceWebApp.TopicKey, ct))!;
        for (var index = 0; index < TangentGovernance.MaximumDirectoryScan; index++)
        {
            var topic = new Topic
            {
                Id = $"bulk-{index:000}", TangentKey = template.TangentKey,
                Title = $"Bulk {index}", CreatorParticipantId = template.CreatorParticipantId,
                CreatedAt = template.CreatedAt.AddSeconds(index + 1), UpdatedAt = template.UpdatedAt,
                PolicyRevision = 1, Access = AccessMap.TopicDefaults()
            };
            await topic.Save(ct);
        }
        app.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", app.OwnerToken);
        using var response = await app.Http.GetAsync("/api/v1/tangents/proj", ct);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var summary = document.RootElement.GetProperty("summary");
        Assert.True(summary.GetProperty("incomplete").GetBoolean());
        Assert.True(summary.GetProperty("topics").GetProperty("atLeast").GetBoolean());
        Assert.Equal(TangentGovernance.MaximumDirectoryScan,
            summary.GetProperty("topics").GetProperty("value").GetInt64());
        Assert.True(summary.GetProperty("posts").GetProperty("atLeast").GetBoolean());
    }
}
