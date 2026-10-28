using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Community;
using Tangent.Conversation;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class ArticlePresentationTests
{
    [Fact]
    public async Task Article_collection_pages_follow_native_creation_order()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        using var owner = new HttpClient { BaseAddress = new Uri(app.Origin) };
        owner.DefaultRequestHeaders.Authorization = new("Bearer", app.OwnerToken);
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        using (var kind = await owner.PatchAsJsonAsync("/api/v1/tangents/proj", new { kind = "articles" }, ct)) kind.EnsureSuccessStatusCode();
        await app.Services.GetRequiredService<TangentGovernance>().SetAccess(app.OwnerParticipantId, "proj", new AccessMap(), ct);
        var start = DateTimeOffset.UtcNow.AddDays(-2);
        for (var index = 0; index < 101; index++)
            await new Topic
            {
                Id = $"page-{index:000}", TangentKey = "proj", Title = $"Article {index:000}",
                CreatorParticipantId = app.OwnerParticipantId, ReadAudience = TopicReadAudience.Public,
                CreatedAt = start.AddMinutes(index), UpdatedAt = start.AddMinutes(index), PolicyRevision = 1,
            }.Save(ct);
        var topics = app.Services.GetRequiredService<TopicGovernance>();
        var first = await topics.ListForTangent(null, "proj", 1, ct);
        var second = await topics.ListForTangent(null, "proj", 2, ct);
        Assert.Equal(100, first.Topics.Count);
        Assert.Equal("page-100", first.Topics[0].Key);
        Assert.Equal(2, first.NextPage);
        Assert.Single(second.Topics);
        Assert.Equal("page-000", second.Topics[0].Key);
        using (var htmlPage = await anonymous.GetAsync("/t/proj/topics?page=2", ct))
        {
            htmlPage.EnsureSuccessStatusCode();
            var html = await htmlPage.Content.ReadAsStringAsync(ct);
            Assert.Contains("Article 000", html);
            Assert.Contains("rel=\"prev\" href=\"/t/proj/topics\"", html);
            Assert.DoesNotContain("Article 100", html);
        }
    }

    [Fact]
    public async Task Article_cards_use_original_opening_post_and_clear_its_removed_cover()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        using var owner = new HttpClient { BaseAddress = new Uri(app.Origin) };
        owner.DefaultRequestHeaders.Authorization = new("Bearer", app.OwnerToken);
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        using (var kind = await owner.PatchAsJsonAsync("/api/v1/tangents/proj", new { kind = "articles" }, ct)) kind.EnsureSuccessStatusCode();
        const string topicKey = "article-card-source";
        using (var create = await owner.PostAsJsonAsync("/api/v1/tangents/proj/topics",
            new { key = topicKey, title = "The original title", topic = "Deck" }, ct)) create.EnsureSuccessStatusCode();
        var png = TestImage.Png();
        using var upload = await owner.PostAsJsonAsync("/api/media", new { tangentKey = "proj", topicKey,
            fileName = "cover.png", content = png }, ct);
        upload.EnsureSuccessStatusCode();
        using var uploaded = JsonDocument.Parse(await upload.Content.ReadAsStringAsync(ct));
        var cover = uploaded.RootElement.GetProperty("url").GetString()!;
        var conversation = app.Services.GetRequiredService<ConversationService>();
        var opening = await conversation.CreatePost(app.OwnerParticipantId, topicKey,
            new PostCreateRequest("article-cover-opening", $"# Opening\n\n![Cover]({cover})\n\nBody text.", Format: PostContent.Markdown), ct);
        var reply = await conversation.CreatePost(app.AgentParticipantId, topicKey,
            new PostCreateRequest("article-cover-reply", "Later reply", ReplyTo: opening.Id), ct);
        await app.Services.GetRequiredService<TangentGovernance>().SetAccess(app.OwnerParticipantId, "proj", new AccessMap(), ct);
        Assert.True((await app.Services.GetRequiredService<TopicGovernance>().SetReadAudience(app.OwnerParticipantId,
            topicKey, TopicReadAudience.Public, true, ct)).Accepted);

        using (var listed = await anonymous.GetAsync("/api/v1/tangents/proj/topics", ct))
        {
            listed.EnsureSuccessStatusCode();
            var body = await listed.Content.ReadAsStringAsync(ct);
            Assert.DoesNotContain("Project Z", body);
            using var json = JsonDocument.Parse(body);
            var card = json.RootElement.GetProperty("topics").EnumerateArray().Single(row => row.GetProperty("key").GetString() == topicKey);
            var article = card.GetProperty("summary").GetProperty("article");
            Assert.Equal(opening.Id, article.GetProperty("openingPostId").GetString());
            Assert.Equal(cover, article.GetProperty("coverImageUrl").GetString());
            Assert.Equal(app.OwnerParticipantId, article.GetProperty("author").GetProperty("id").GetString());
            Assert.Equal(opening.AcceptedAt, article.GetProperty("createdAt").GetDateTimeOffset());
            Assert.True(article.GetProperty("readingMinutes").GetInt32() >= 1);
            Assert.False(article.GetProperty("removed").GetBoolean());
            Assert.DoesNotContain(reply.Id, article.ToString());
        }
        using (var page = await anonymous.GetAsync("/t/proj/topics", ct))
        {
            page.EnsureSuccessStatusCode();
            var html = await page.Content.ReadAsStringAsync(ct);
            Assert.Contains("class=\"article-collection\"", html);
            Assert.Contains("The original title", html);
            Assert.Contains(cover, html);
            Assert.Contains($"href=\"/t/proj/topics/{topicKey}\"", html);
            Assert.DoesNotContain("Project Z", html);
        }
        await conversation.ChangePost(app.OwnerParticipantId, topicKey, opening.Id, null, true, ct);
        using (var listed = await anonymous.GetAsync("/api/v1/tangents/proj/topics", ct))
        {
            listed.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await listed.Content.ReadAsStringAsync(ct));
            var card = json.RootElement.GetProperty("topics").EnumerateArray().Single(row => row.GetProperty("key").GetString() == topicKey);
            var article = card.GetProperty("summary").GetProperty("article");
            Assert.Equal(opening.Id, article.GetProperty("openingPostId").GetString());
            Assert.True(article.GetProperty("removed").GetBoolean());
            Assert.False(article.TryGetProperty("coverImageUrl", out var removedCover)
                && removedCover.ValueKind != JsonValueKind.Null);
            Assert.DoesNotContain("Opening", article.ToString());
            Assert.DoesNotContain("Body text", article.ToString());
        }
        using (var media = await anonymous.GetAsync(cover, ct)) Assert.Equal(HttpStatusCode.NotFound, media.StatusCode);
    }

    [Fact]
    public async Task New_article_allows_its_author_to_edit_and_preserves_explicit_restrictions()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        using var owner = new HttpClient { BaseAddress = new Uri(app.Origin) };
        owner.DefaultRequestHeaders.Authorization = new("Bearer", app.OwnerToken);
        using (var kind = await owner.PatchAsJsonAsync("/api/v1/tangents/proj", new { kind = "articles" }, ct)) kind.EnsureSuccessStatusCode();
        using var start = await app.Http.PostAsJsonAsync("/api/v1/experience/tangents/proj/topics",
            new { requestId = "article-edit-default", title = "Editable article", text = "# First draft", format = "markdown" }, ct);
        start.EnsureSuccessStatusCode();
        using var created = JsonDocument.Parse(await start.Content.ReadAsStringAsync(ct));
        var topicKey = created.RootElement.GetProperty("topicRef").GetString()!.Split("::")[^1];
        var postId = created.RootElement.GetProperty("postRef").GetString()!.Split("::")[^1];

        using (var read = await app.Http.GetAsync($"/api/v1/experience/topics/{topicKey}", ct))
        {
            read.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync(ct));
            Assert.True(json.RootElement.GetProperty("allowPostEditing").GetBoolean());
        }

        using (var otherAuthor = await owner.PatchAsJsonAsync($"/api/v1/experience/posts/{postId}",
            new { text = "Owner rewrite" }, ct))
            Assert.Equal(HttpStatusCode.Forbidden, otherAuthor.StatusCode);

        using (var edit = await app.Http.PatchAsJsonAsync($"/api/v1/experience/posts/{postId}",
            new { text = "## Second draft", format = "markdown" }, ct))
            edit.EnsureSuccessStatusCode();
        using (EntityContext.NoCache())
        {
            var updated = (await Post.Get(postId, ct))!;
            Assert.Equal("## Second draft", updated.Content.Text);
            Assert.NotNull(updated.EditedAt);
            var previous = (await Post.Get(updated.ChangeId!, Post.ChangelogPartition, ct))!;
            Assert.Equal("# First draft", previous.Content.Text);
            Assert.Equal(app.AgentParticipantId, previous.AuthorParticipantId);
        }

        using (var settings = await owner.PatchAsJsonAsync($"/api/v1/tangents/proj/topics/{topicKey}/settings",
            new { allowPostEditing = false }, ct))
            settings.EnsureSuccessStatusCode();
        using (var disabled = await app.Http.PatchAsJsonAsync($"/api/v1/experience/posts/{postId}",
            new { text = "## Third draft" }, ct))
            Assert.Equal(HttpStatusCode.Forbidden, disabled.StatusCode);
        using (EntityContext.NoCache())
            Assert.Equal("## Second draft", (await Post.Get(postId, ct))!.Content.Text);
    }

    [Fact]
    public async Task Tangent_kind_changes_all_topics_without_changing_posts_or_request_identity()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var request = new { requestId = "article-start-1", title = "An Article", text = "# The opening", format = "markdown" };
        using var start = await app.Http.PostAsJsonAsync("/api/v1/experience/tangents/proj/topics", request, ct);
        start.EnsureSuccessStatusCode();
        using var created = JsonDocument.Parse(await start.Content.ReadAsStringAsync(ct));
        var data = created.RootElement;
        var topicRef = data.GetProperty("topicRef").GetString()!;
        var postRef = data.GetProperty("postRef").GetString()!;
        var topicKey = topicRef.Split("::")[^1];
        using (var read = await app.Http.GetAsync($"/api/v1/experience/topics/{topicKey}", ct))
        {
            read.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync(ct));
            Assert.Equal("conversation", json.RootElement.GetProperty("presentation").GetString());
        }
        using var owner = new HttpClient { BaseAddress = new Uri(app.Origin) };
        owner.DefaultRequestHeaders.Authorization = new("Bearer", app.OwnerToken);
        using (var kind = await owner.PatchAsJsonAsync("/api/v1/tangents/proj", new { kind = "articles" }, ct)) kind.EnsureSuccessStatusCode();
        using (var read = await app.Http.GetAsync($"/api/v1/experience/topics/{topicKey}", ct))
        {
            read.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync(ct));
            Assert.Equal("article", json.RootElement.GetProperty("presentation").GetString());
            Assert.Equal(postRef, json.RootElement.GetProperty("openingPost").GetProperty("ref").GetString());
        }
        using (var replay = await app.Http.PostAsJsonAsync("/api/v1/experience/tangents/proj/topics", request, ct))
        {
            replay.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await replay.Content.ReadAsStringAsync(ct));
            Assert.Equal(topicRef, json.RootElement.GetProperty("topicRef").GetString());
            Assert.Equal(postRef, json.RootElement.GetProperty("postRef").GetString());
        }
        using (var invalid = await owner.PatchAsJsonAsync("/api/v1/tangents/proj", new { kind = "magazine" }, ct))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var childOverride = await owner.PatchAsJsonAsync($"/api/v1/tangents/proj/topics/{topicKey}/settings",
            new { presentation = "conversation" }, ct)) Assert.Equal(HttpStatusCode.BadRequest, childOverride.StatusCode);
        using (var metadata = await app.Http.GetAsync($"/api/v1/tangents/proj/topics/{topicKey}", ct))
        {
            metadata.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await metadata.Content.ReadAsStringAsync(ct));
            Assert.Equal("article", json.RootElement.GetProperty("presentation").GetString());
        }
        using (var sibling = await app.Http.GetAsync($"/api/v1/tangents/proj/topics/{ExperienceWebApp.TopicKey}", ct))
        {
            sibling.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await sibling.Content.ReadAsStringAsync(ct));
            Assert.Equal("article", json.RootElement.GetProperty("presentation").GetString());
        }
        using (var kind = await owner.PatchAsJsonAsync("/api/v1/tangents/proj", new { kind = "discussions" }, ct)) kind.EnsureSuccessStatusCode();
        using (var read = await app.Http.GetAsync($"/api/v1/experience/topics/{topicKey}", ct))
        {
            read.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync(ct));
            Assert.Equal("conversation", json.RootElement.GetProperty("presentation").GetString());
        }
        Assert.Equal("# The opening", (await Post.Get(postRef.Split("::")[^1], ct))!.Content.Text);
    }

    [Fact]
    public async Task Opening_stays_canonical_across_pages_and_removal_with_public_article_html()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var topicKey = "article-paging";
        using var owner = new HttpClient { BaseAddress = new Uri(app.Origin) };
        owner.DefaultRequestHeaders.Authorization = new("Bearer", app.OwnerToken);
        using (var kind = await owner.PatchAsJsonAsync("/api/v1/tangents/proj", new { kind = "articles" }, ct)) kind.EnsureSuccessStatusCode();
        using (var create = await owner.PostAsJsonAsync("/api/v1/tangents/proj/topics",
            new { key = topicKey, title = "A long story", topic = "The story deck" }, ct))
            create.EnsureSuccessStatusCode();
        var posts = app.Services.GetRequiredService<ConversationService>();
        var opening = await posts.CreatePost(app.AgentParticipantId, topicKey,
            new PostCreateRequest("article-opening", "## Section One\n\n" + new string('<', 50_000), Format: PostContent.Markdown), ct);
        var reply = await posts.CreatePost(app.AgentParticipantId, topicKey,
            new PostCreateRequest("article-reply", "A reply", ReplyTo: opening.Id), ct);
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        using (var hidden = await anonymous.GetAsync($"/t/proj/topics/{topicKey}", ct))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        using (var page = await app.Http.GetAsync($"/api/topics/{topicKey}/posts?after={opening.Id}", ct))
        {
            page.EnsureSuccessStatusCode();
            var body = await page.Content.ReadAsByteArrayAsync(ct);
            Assert.True(body.Length <= 2 * WindowPlanner.ResultBudget, $"Article history: {body.Length} bytes");
            using var json = JsonDocument.Parse(body);
            Assert.Equal(opening.Id, json.RootElement.GetProperty("openingPost").GetProperty("id").GetString());
            Assert.DoesNotContain(json.RootElement.GetProperty("messages").EnumerateArray(), row => row.GetProperty("id").GetString() == opening.Id);
        }
        using (var first = await app.Http.GetAsync($"/api/topics/{topicKey}/posts?from=start", ct))
        {
            first.EnsureSuccessStatusCode();
            var body = await first.Content.ReadAsByteArrayAsync(ct);
            Assert.True(body.Length <= WindowPlanner.ArticleResultBudget, $"Article history with duplicate opening: {body.Length} bytes");
            using var json = JsonDocument.Parse(body);
            Assert.Contains(json.RootElement.GetProperty("messages").EnumerateArray(), row => row.GetProperty("id").GetString() == opening.Id);
            Assert.Equal(opening.Id, json.RootElement.GetProperty("openingPost").GetProperty("id").GetString());
        }
        using (var deep = await app.Http.GetAsync($"/api/v1/tangents/proj/posts/{reply.Id}", ct))
        {
            deep.EnsureSuccessStatusCode();
            var body = await deep.Content.ReadAsByteArrayAsync(ct);
            Assert.True(body.Length <= 2 * WindowPlanner.ResultBudget, $"Article deep window: {body.Length} bytes");
            using var json = JsonDocument.Parse(body);
            Assert.Equal(opening.Id, json.RootElement.GetProperty("window").GetProperty("openingPost").GetProperty("id").GetString());
        }
        await app.Services.GetRequiredService<TangentGovernance>().SetAccess(app.OwnerParticipantId,
            ExperienceWebApp.TangentKey, new AccessMap(), ct);
        Assert.True((await app.Services.GetRequiredService<TopicGovernance>().SetReadAudience(app.OwnerParticipantId,
            topicKey, TopicReadAudience.Public, true, ct)).Accepted);
        using (var publicRead = await anonymous.GetAsync($"/api/v1/public/tangents/proj/topics/{topicKey}/posts?around={reply.Id}", ct))
        {
            publicRead.EnsureSuccessStatusCode();
            var body = await publicRead.Content.ReadAsByteArrayAsync(ct);
            Assert.True(body.Length <= 2 * WindowPlanner.ResultBudget, $"Article public window: {body.Length} bytes");
            using var json = JsonDocument.Parse(body);
            Assert.Equal(opening.Id, json.RootElement.GetProperty("openingPost").GetProperty("id").GetString());
        }
        using (var document = await anonymous.GetAsync($"/t/proj/{reply.Id}", ct))
        {
            document.EnsureSuccessStatusCode();
            var html = await document.Content.ReadAsStringAsync(ct);
            Assert.Contains("<h1>A long story</h1>", html);
            Assert.Contains("id=\"article-heading-1\"", html);
            Assert.Contains($"href=\"#post-{reply.Id}\"", html);
            Assert.Contains("Replies", html);
            Assert.Equal(1, html.Split($"id=\"post-{opening.Id}\"").Length - 1);
        }
        Assert.True((await app.Services.GetRequiredService<TopicGovernance>().SetSettings(app.OwnerParticipantId,
            topicKey, true, null, null, null, ct)).Accepted);
        await posts.ChangePost(app.OwnerParticipantId, topicKey, opening.Id, null, true, ct);
        using (var tombstone = await anonymous.GetAsync($"/api/v1/public/tangents/proj/topics/{topicKey}/posts?around={reply.Id}", ct))
        {
            tombstone.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await tombstone.Content.ReadAsStringAsync(ct));
            var opener = json.RootElement.GetProperty("openingPost");
            Assert.Equal(opening.Id, opener.GetProperty("id").GetString());
            Assert.True(opener.GetProperty("removed").GetBoolean());
            Assert.False(opener.TryGetProperty("text", out var text) && text.ValueKind != JsonValueKind.Null);
        }
    }
}
