using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Koan.Data.Core;
using Tangent.Identity;
using Tangent.Community;
using Tangent.Access;
using Tangent.Conversation;
using Tangent.Activity;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

/// <summary>Exercises the ordinary HTTP resource API used by authorized browser and Forum clients.</summary>
[Xunit.Collection("Experience integration")]
public sealed class ConnectorWireTests
{
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.Clone();
    }

    private static void UseToken(HttpClient http, string token)
        => http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static void AssertPlainResource(JsonElement body)
    {
        foreach (var field in new[] { "experienceVersion", "operation", "result", "snapshot", "actions",
            "text", "structuredContent", "isError" })
            Assert.False(body.TryGetProperty(field, out _), $"Resource response exposed {field}: {body}");
    }

    [Fact]
    public async Task Resource_routes_return_plain_data_and_the_operation_catalog_is_absent()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        using var catalog = await app.Http.GetAsync("/api/v1/experience/operations", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, catalog.StatusCode);
        using var read = await app.Http.GetAsync("/api/v1/experience/topics/proj-z?limit=2", TestContext.Current.CancellationToken);
        read.EnsureSuccessStatusCode();
        var result = await ReadAsync(read);
        Assert.True(result.TryGetProperty("posts", out var posts));
        Assert.True(posts.GetArrayLength() <= 2);
        Assert.False(result.TryGetProperty("structuredContent", out _));
        Assert.False(result.TryGetProperty("operation", out _));

        using var missingRetryId = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/posts",
            new { text = "This must not be posted." }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingRetryId.StatusCode);
        Assert.Equal("requestId", (await ReadAsync(missingRetryId)).GetProperty("field").GetString());
    }

    [Fact]
    public async Task Role_grants_cannot_override_topic_or_tangent_membership_limits()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        var topics = app.Services.GetRequiredService<TopicGovernance>();
        var tangents = app.Services.GetRequiredService<TangentGovernance>();
        var ct = TestContext.Current.CancellationToken;
        const string delegatedTopic = "delegated-creator-boundary";
        await tangents.CreateTopic(app.AgentParticipantId, ExperienceWebApp.TangentKey,
            delegatedTopic, "Delegated creator boundary", "Scoped creator authority", ct);
        Assert.True((await topics.ReadPolicyAsync(app.AgentParticipantId, delegatedTopic, ct)).CanManage);
        await topics.SetMembership(app.OwnerParticipantId, delegatedTopic,
            app.AgentParticipantId, TopicRole.Removed, ct);
        var removedCreator = await topics.ReadPolicyAsync(app.AgentParticipantId, delegatedTopic, ct);
        Assert.False(removedCreator.CanRead);
        Assert.False(removedCreator.CanManage);

        async Task AssertAccess(HttpStatusCode readStatus, HttpStatusCode postStatus, string requestId)
        {
            using var read = await app.Http.GetAsync("/api/v1/experience/topics/proj-z", ct);
            Assert.Equal(readStatus, read.StatusCode);
            using var post = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/posts",
                new { requestId, text = "A scoped policy must bound my server role." }, ct);
            Assert.Equal(postStatus, post.StatusCode);
        }

        await AssertAccess(HttpStatusCode.OK, HttpStatusCode.OK, "policy-member-allowed");
        await topics.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            app.AgentParticipantId, TopicRole.Reader, ct);
        await AssertAccess(HttpStatusCode.OK, HttpStatusCode.Forbidden, "policy-topic-reader");
        await topics.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            app.AgentParticipantId, TopicRole.Removed, ct);
        await AssertAccess(HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, "policy-topic-removed");
        await topics.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            app.AgentParticipantId, TopicRole.Member, ct);
        await tangents.SetAccess(app.OwnerParticipantId, ExperienceWebApp.TangentKey,
            new AccessMap { See = ["role:member"], Post = ["role:member"],
                Manage = ["role:member"], CreateTopics = ["role:member"] }, ct);
        await tangents.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TangentKey,
            ExperienceWebApp.AgentDid, TangentRole.Removed, ct);
        await AssertAccess(HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, "policy-tangent-removed");
        using var scoped = await app.Http.GetAsync("/api/v1/experience?scopeRef=" +
            Uri.EscapeDataString($"{app.Origin}::{ExperienceWebApp.TangentKey}"), ct);
        Assert.Equal(HttpStatusCode.Forbidden, scoped.StatusCode);
        var (credential, manageToken) = ParticipantCredential.Issue(app.AgentParticipantId,
            "Removed member with server role", 1,
            [ParticipationGrants.Read, ParticipationGrants.Post, ParticipationGrants.Manage], DateTimeOffset.UtcNow);
        using (EntityContext.NoCache()) await credential.Save(ct);
        UseToken(app.Http, manageToken);
        using var card = await app.Http.GetAsync("/api/v1/tangents/proj", ct);
        card.EnsureSuccessStatusCode(); // Tangent cards remain discoverable.
        Assert.False((await ReadAsync(card)).GetProperty("canManage").GetBoolean());
        using var access = await app.Http.GetAsync("/api/v1/tangents/proj/access", ct);
        Assert.Equal(HttpStatusCode.Forbidden, access.StatusCode);
        using var change = await app.Http.PatchAsJsonAsync("/api/v1/tangents/proj",
            new { description = "A removed actor cannot change the card." }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        using var createTopic = await app.Http.PostAsJsonAsync("/api/v1/tangents/proj/topics",
            new { key = "removed-cannot-create", title = "Denied", topic = "" }, ct);
        Assert.Equal(HttpStatusCode.Forbidden, createTopic.StatusCode);
    }

    [Fact]
    public async Task Arrival_exposes_current_grants_and_management_facts()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        using var participant = await app.Http.GetAsync("/api/v1/experience", TestContext.Current.CancellationToken);
        participant.EnsureSuccessStatusCode();
        var ordinary = await ReadAsync(participant);
        var ordinaryAuthority = ordinary.GetProperty("authority");
        Assert.True(ordinaryAuthority.GetProperty("canRead").GetBoolean());
        Assert.True(ordinaryAuthority.GetProperty("canPost").GetBoolean());
        Assert.False(ordinaryAuthority.GetProperty("canManage").GetBoolean());
        Assert.False(ordinaryAuthority.GetProperty("canStewardScope").GetBoolean());
        Assert.False(ordinaryAuthority.GetProperty("management").GetProperty("canWarn").GetBoolean());
        Assert.False(ordinary.GetProperty("place").TryGetProperty("allowedActions", out _));

        UseToken(app.Http, app.OwnerToken);
        using var owner = await app.Http.GetAsync("/api/v1/experience", TestContext.Current.CancellationToken);
        owner.EnsureSuccessStatusCode();
        var current = await ReadAsync(owner);
        Assert.Equal("owner", current.GetProperty("place").GetProperty("role").GetString());
        Assert.True(current.GetProperty("authority").GetProperty("canManage").GetBoolean());
        Assert.False(current.GetProperty("authority").GetProperty("canStewardScope").GetBoolean());
        Assert.True(current.GetProperty("authority").GetProperty("management").GetProperty("canWarn").GetBoolean());
    }

    [Fact]
    public async Task Conversation_history_has_no_second_post_or_read_position_write_route()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        using var post = await app.Http.PostAsJsonAsync("/api/topics/proj-z/posts",
            new { operationId = "duplicate-route", text = "This must not be posted." },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        using var acknowledge = await app.Http.PostAsJsonAsync("/api/topics/proj-z/read-position",
            new { cursor = "invalid" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, acknowledge.StatusCode);
    }

    [Fact]
    public async Task Bearer_credential_cannot_open_or_write_the_cookie_role_editor()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.OwnerToken);
        using var session = await app.Http.GetAsync("/api/roles/ui/session", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
        using var change = new HttpRequestMessage(HttpMethod.Put,
            $"/api/roles/ui/participant/{app.HumanParticipantId}/roles/role:member");
        change.Headers.TryAddWithoutValidation("RequestVerificationToken", "harvested-browser-token");
        using var denied = await app.Http.SendAsync(change, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Canonical_post_write_rejects_cross_origin_cookie_and_accepts_bearer()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ParticipationConstants.ParticipantClaim, app.AgentParticipantId)], "browser"));
        var cookieScheme = Koan.Web.Auth.Extensions.AuthenticationExtensions.CookieScheme;
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(cookieScheme);
        var cookie = options.Cookie.Name + "=" + options.TicketDataFormat.Protect(
            new AuthenticationTicket(principal, cookieScheme));
        Assert.NotEmpty(cookie);
        using var browser = new HttpClient { BaseAddress = new Uri(app.Origin) };
        using var crossOrigin = new HttpRequestMessage(HttpMethod.Post, "/api/v1/experience/topics/proj-z/posts")
        {
            Content = JsonContent.Create(new { requestId = "cross-origin-cookie-post", text = "Must not land." })
        };
        crossOrigin.Headers.TryAddWithoutValidation("Cookie", cookie);
        crossOrigin.Headers.TryAddWithoutValidation("Origin", "https://unrelated.example");
        using var rejected = await browser.SendAsync(crossOrigin, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        UseToken(app.Http, app.AgentToken);
        using var accepted = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/posts",
            new { requestId = "same-route-bearer-post", text = "Bearer grant and Topic policy permit this." },
            TestContext.Current.CancellationToken);
        accepted.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("?checkpoint=invalid")]
    [InlineData("?pageCursor=invalid&checkpoint=invalid")]
    public async Task Invalid_digest_continuations_do_not_restart_at_the_newest_page(string query)
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        using var response = await app.Http.GetAsync("/api/v1/experience/updates" + query,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("cursor_expired", (await ReadAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task History_continuation_cursor_is_a_bad_read_position_request()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        var protector = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Cursor.v1");
        var continuation = protector.Protect(JsonSerializer.Serialize(new ConversationCursor(
            app.AgentParticipantId, "proj-z", 1, 3, DateTimeOffset.UtcNow.AddDays(1))));

        using var rejected = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/read-position",
            new { readCursor = continuation }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("invalid_arguments", (await ReadAsync(rejected)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Forum_reads_writes_moderation_sources_and_problems_use_plain_resource_bodies()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.OwnerToken);
        var paths = new[]
        {
            "/api/v1/experience?scopeRef=", "/api/v1/experience/tangents",
            "/api/v1/experience/tangents/proj/topics", "/api/v1/experience/topics/proj-z?limit=2",
            "/api/v1/experience/updates", "/api/v1/experience/participants/" + ExperienceWebApp.AgentDid,
            "/api/participants/" + ExperienceWebApp.AgentDid + "/profile",
            "/api/v1/experience/topics/proj-z/moderation/cases?page=1",
            "/api/v1/content/sources"
        };
        foreach (var path in paths)
        {
            using var response = await app.Http.GetAsync(path, TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            AssertPlainResource(await ReadAsync(response));
        }
        using var created = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/posts",
            new { requestId = "plain-resource-post", text = "A plain response." }, TestContext.Current.CancellationToken);
        created.EnsureSuccessStatusCode();
        AssertPlainResource(await ReadAsync(created));
        using var invalid = await app.Http.GetAsync("/api/v1/experience/topics/proj-z?cursor=invalid",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var problem = await ReadAsync(invalid);
        AssertPlainResource(problem);
        Assert.Equal("cursor_expired", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Authenticated_history_accepts_a_post_identity_as_its_after_edge()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        using var page = await app.Http.GetAsync("/api/topics/proj-z/posts?after=m-agent-q1",
            TestContext.Current.CancellationToken);
        page.EnsureSuccessStatusCode();
        var history = await ReadAsync(page);
        Assert.Equal(2, history.GetProperty("messages").GetArrayLength());
        Assert.Equal("m-leo-r1", history.GetProperty("messages")[0].GetProperty("id").GetString());
        Assert.False(string.IsNullOrEmpty(history.GetProperty("resumeCursor").GetString()));
        using var foreign = await app.Http.GetAsync("/api/topics/proj-z/posts?after=not-a-post",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
    }

    [Fact]
    public async Task Public_history_pages_by_post_identity_and_rejects_a_foreign_edge()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        using (EntityContext.NoCache())
        {
            var tangent = (await Tangent.Community.Tangent.Get(ExperienceWebApp.TangentKey))!;
            tangent.Access = new AccessMap { See = [AccessTokens.Everyone], Post = ["role:member"] };
            await tangent.Save();
            var topic = (await Topic.Get(ExperienceWebApp.TopicKey))!;
            topic.ReadAudience = TopicReadAudience.Public;
            await topic.Save();
        }
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        var path = $"/api/v1/public/tangents/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}/posts?limit=2";
        using var latest = await anonymous.GetAsync(path, TestContext.Current.CancellationToken);
        latest.EnsureSuccessStatusCode();
        var latestWindow = await ReadAsync(latest);
        var olderId = latestWindow.GetProperty("olderBefore").GetString();
        Assert.StartsWith("m-", olderId);
        using var previous = await anonymous.GetAsync(path + "&before=" + Uri.EscapeDataString(olderId!),
            TestContext.Current.CancellationToken);
        previous.EnsureSuccessStatusCode();
        var previousWindow = await ReadAsync(previous);
        Assert.Single(previousWindow.GetProperty("posts").EnumerateArray());
        Assert.Equal("m-agent-q1", previousWindow.GetProperty("posts")[0].GetProperty("id").GetString());
        using var next = await anonymous.GetAsync(path + "&after=m-agent-q1", TestContext.Current.CancellationToken);
        next.EnsureSuccessStatusCode();
        Assert.Equal(2, (await ReadAsync(next)).GetProperty("posts").GetArrayLength());
        using var invalid = await anonymous.GetAsync(path + "&after=999999", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using (EntityContext.NoCache())
        {
            var tangent = (await Tangent.Community.Tangent.Get(ExperienceWebApp.TangentKey))!;
            tangent.Access = new AccessMap { See = ["role:member"], Post = ["role:member"] };
            await tangent.Save();
        }
        using var hiddenJson = await anonymous.GetAsync(path, TestContext.Current.CancellationToken);
        using var hiddenHtml = await anonymous.GetAsync($"/t/{ExperienceWebApp.TangentKey}/topics/{ExperienceWebApp.TopicKey}",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, hiddenJson.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hiddenHtml.StatusCode);
    }

    [Fact]
    public async Task Bluesky_refresh_keeps_a_stable_local_projection_and_pages_without_remote_calls()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        const string did = "did:plc:contentauthorAAAAAA";
        const string handle = "author.bsky.social";
        var requests = 0;
        var client = new HttpClient(new FeedHandler(request =>
        {
            requests++;
            if (request.RequestUri!.AbsolutePath.EndsWith("getProfile", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"did\":\"" + did + "\"}", Encoding.UTF8, "application/json") };
            var feed = System.Text.Json.JsonSerializer.Serialize(new
            {
                feed = Enumerable.Range(1, 3).Reverse().Select(number => new
                {
                    post = new
                    {
                        uri = $"at://{did}/app.bsky.feed.post/{number}", cid = "cid-" + number,
                        author = new { did, handle },
                        record = new { text = "Bluesky post " + number,
                            createdAt = new DateTimeOffset(2026, 9, 20, 12, number, 0, TimeSpan.Zero).ToString("O") }
                    }
                }),
                cursor = (string?)null
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(feed, Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://public.api.bsky.app") };
        var source = new BlueskyContentSource(new FeedFactory(client), TimeProvider.System);
        var first = await source.Refresh(handle, TestContext.Current.CancellationToken);
        Assert.Equal(did, first.ActorDid);
        Assert.Equal(3, first.Imported);
        Assert.Equal("disconnected", first.Publish);
        var page = (await source.Read(new ContentQuery(null, null, did, null, null, 2),
            TestContext.Current.CancellationToken))!;
        Assert.Equal(2, page.Posts.Count);
        Assert.NotNull(page.OlderBefore);
        Assert.Equal(2, requests); // one profile resolution and one feed fetch
        var older = (await source.Read(new ContentQuery(null, null, did, page.OlderBefore, null, 2),
            TestContext.Current.CancellationToken))!;
        Assert.Single(older.Posts);
        Assert.Equal(2, requests); // projection reads stay local
        var again = await source.Refresh(did, TestContext.Current.CancellationToken);
        Assert.Equal(0, again.Imported);

        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        using var authors = await anonymous.GetAsync("/api/v1/content/bluesky/authors?limit=2",
            TestContext.Current.CancellationToken);
        authors.EnsureSuccessStatusCode();
        var discovered = (await ReadAsync(authors)).GetProperty("authors");
        Assert.Contains(discovered.EnumerateArray(), item => item.GetProperty("actorDid").GetString() == did);
        using var document = await anonymous.GetAsync("/sources/bluesky/" + Uri.EscapeDataString(did),
            TestContext.Current.CancellationToken);
        document.EnsureSuccessStatusCode();
        Assert.Contains("Bluesky post 3", await document.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        UseToken(app.Http, app.AgentToken);
        using var throughCompanion = await app.Http.GetAsync(
            "/api/v1/content/bluesky/posts?actorDid=" + Uri.EscapeDataString(did) + "&limit=2",
            TestContext.Current.CancellationToken);
        throughCompanion.EnsureSuccessStatusCode();
        var invoked = await ReadAsync(throughCompanion);
        Assert.Equal(2, invoked.GetProperty("posts").GetArrayLength());
        using var ownerOnly = await app.Http.PostAsJsonAsync(
            "/api/v1/content/bluesky/authors/" + Uri.EscapeDataString(did) + "/refresh",
            new { }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, ownerOnly.StatusCode);
        Assert.Equal(3, requests); // discovery and no-JS page use stored projection only
    }

    [Fact]
    public async Task Bluesky_refresh_resumes_a_full_feed_sweep_without_skipping_middle_pages()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        const string did = "did:plc:busycontentauthorAAAAAA";
        var expanded = false;
        var client = new HttpClient(new FeedHandler(request =>
        {
            var query = request.RequestUri!.Query;
            var cursor = query.Contains("cursor=", StringComparison.Ordinal)
                ? int.Parse(Uri.UnescapeDataString(query.Split("cursor=", 2)[1])) : 0;
            var ids = expanded ? Enumerable.Range(0, 61).Reverse().ToArray() : [0];
            var posts = ids.Skip(cursor).Take(25).Select(number => new
            {
                post = new
                {
                    uri = $"at://{did}/app.bsky.feed.post/{number}", cid = "cid-" + number,
                    author = new { did, handle = "busy.bsky.social" },
                    record = new { text = "Post " + number,
                        createdAt = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero)
                            .AddMinutes(number).ToString("O") }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                { feed = posts, cursor = cursor + 25 < ids.Length ? (cursor + 25).ToString() : null }),
                    Encoding.UTF8, "application/json")
            };
        })) { BaseAddress = new Uri("https://public.api.bsky.app") };
        var source = new BlueskyContentSource(new FeedFactory(client), TimeProvider.System);
        Assert.Equal(1, (await source.Refresh(did, TestContext.Current.CancellationToken)).Imported);
        expanded = true;
        var first = await source.Refresh(did, TestContext.Current.CancellationToken);
        Assert.Equal(25, first.Imported);
        Assert.Equal("sweep_available", first.Backfill);
        var second = await source.Refresh(did, TestContext.Current.CancellationToken);
        Assert.Equal(25, second.Imported);
        Assert.Equal("sweep_available", second.Backfill);
        var third = await source.Refresh(did, TestContext.Current.CancellationToken);
        Assert.Equal(10, third.Imported);
        Assert.Equal("complete", third.Backfill);
        var count = 0;
        string? edge = null;
        do
        {
            var page = (await source.Read(new ContentQuery(null, null, did, edge, null, 25),
                TestContext.Current.CancellationToken))!;
            count += page.Posts.Count;
            edge = page.OlderBefore;
        } while (edge is not null);
        Assert.Equal(61, count);
    }

    [Fact]
    public async Task Bluesky_full_sweep_updates_changed_records_and_hides_disappeared_posts()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        const string did = "did:plc:editedcontentauthorAAAAAA";
        var changed = false;
        var client = new HttpClient(new FeedHandler(_ =>
        {
            var numbers = changed ? new[] { 2 } : new[] { 2, 1 };
            var feed = numbers.Select(number => new
            {
                post = new
                {
                    uri = $"at://{did}/app.bsky.feed.post/{number}",
                    cid = changed ? "new-cid-" + number : "old-cid-" + number,
                    author = new { did, handle = "edited.bsky.social" },
                    record = new { text = changed ? "Updated Post " + number : "Original Post " + number,
                        createdAt = new DateTimeOffset(2026, 9, 20, 12, number, 0, TimeSpan.Zero).ToString("O") }
                }
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(new { feed }), Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://public.api.bsky.app") };
        var source = new BlueskyContentSource(new FeedFactory(client), TimeProvider.System);
        Assert.Equal(2, (await source.Refresh(did, TestContext.Current.CancellationToken)).Imported);
        changed = true;
        var reconciled = await source.Refresh(did, TestContext.Current.CancellationToken);
        Assert.Equal("complete", reconciled.Backfill);
        Assert.Equal(1, reconciled.Hidden);
        var page = (await source.Read(new ContentQuery(null, null, did, null, null, 25),
            TestContext.Current.CancellationToken))!;
        Assert.Single(page.Posts);
        Assert.Equal("Updated Post 2", page.Posts[0].Text);
        Assert.Equal("new-cid-2", page.Posts[0].SourceCid);
        using var anonymous = new HttpClient { BaseAddress = new Uri(app.Origin) };
        using var publicPage = await anonymous.GetAsync("/sources/bluesky/" + Uri.EscapeDataString(did),
            TestContext.Current.CancellationToken);
        publicPage.EnsureSuccessStatusCode();
        var html = await publicPage.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Updated Post 2", html);
        Assert.DoesNotContain("Original Post 1", html);
    }

    [Fact]
    public async Task Bluesky_published_post_gets_indexing_grace_then_reconciles_if_absent()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        const string did = "did:plc:publishedauthorAAAAAA";
        var time = new MutableTime(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
        var client = new HttpClient(new FeedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"feed\":[]}", Encoding.UTF8, "application/json") }))
        { BaseAddress = new Uri("https://public.api.bsky.app") };
        var source = new BlueskyContentSource(new FeedFactory(client), time);
        await source.ProjectPublished(did, $"at://{did}/app.bsky.feed.post/3mexamplepost", "cid-1",
            "Published", time.GetUtcNow().AddYears(-1), TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(0, (await source.Refresh(did, TestContext.Current.CancellationToken)).Hidden);
        Assert.Single((await source.Read(new ContentQuery(null, null, did, null, null, 25),
            TestContext.Current.CancellationToken))!.Posts);
        time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, (await source.Refresh(did, TestContext.Current.CancellationToken)).Hidden);
        Assert.Empty((await source.Read(new ContentQuery(null, null, did, null, null, 25),
            TestContext.Current.CancellationToken))!.Posts);
    }

    [Fact]
    public async Task Bluesky_post_id_paging_reaches_both_posts_when_sort_hash_collides()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        const string did = "did:plc:collisionauthorAAAAAA";
        var timestamp = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var seen = new Dictionary<long, string>();
        string? left = null, right = null;
        for (var number = 0; number < 20_000; number++)
        {
            var uri = $"at://{did}/app.bsky.feed.post/candidate{number}";
            var key = BlueskyContentSource.SortKey(timestamp, uri);
            if (seen.TryGetValue(key, out left)) { right = uri; break; }
            seen[key] = uri;
        }
        Assert.NotNull(left);
        Assert.NotNull(right);
        var client = new HttpClient(new FeedHandler(_ => throw new Exception("No remote read expected")))
        { BaseAddress = new Uri("https://public.api.bsky.app") };
        var source = new BlueskyContentSource(new FeedFactory(client), TimeProvider.System);
        await source.ProjectPublished(did, left!, "cid-left", "left", timestamp, TestContext.Current.CancellationToken);
        await source.ProjectPublished(did, right!, "cid-right", "right", timestamp, TestContext.Current.CancellationToken);
        var newest = (await source.Read(new ContentQuery(null, null, did, null, null, 1),
            TestContext.Current.CancellationToken))!;
        Assert.Single(newest.Posts);
        Assert.NotNull(newest.OlderBefore);
        var older = (await source.Read(new ContentQuery(null, null, did, newest.OlderBefore, null, 1),
            TestContext.Current.CancellationToken))!;
        Assert.Single(older.Posts);
        Assert.NotEqual(newest.Posts[0].Id, older.Posts[0].Id);
        Assert.Equal(new[] { "left", "right" }, new[] { newest.Posts[0].Text, older.Posts[0].Text }.Order());
    }

    private sealed class MutableTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }

    [Fact]
    public async Task Bluesky_publish_requires_separate_account_consent()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        using var connection = await app.Http.GetAsync("/api/v1/content/bluesky/connection",
            TestContext.Current.CancellationToken);
        connection.EnsureSuccessStatusCode();
        Assert.False((await ReadAsync(connection)).GetProperty("canPublish").GetBoolean());
        using var connect = await app.Http.GetAsync("/api/v1/content/bluesky/connect?return=%2Ft%2Fproj%2Fm-agent-q1",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, connect.StatusCode); // a bearer cannot start browser OAuth
        using var publish = await app.Http.PostAsJsonAsync("/api/v1/content/bluesky/publish",
            new { postId = "m-agent-q1", requestId = "test-m-agent-q1" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
    }

    [Fact]
    public async Task Bluesky_publish_recovers_a_taken_record_key_or_lost_create_response()
    {
        var accepted = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
        var local = new Post { Id = "local-post-one", AcceptedAt = accepted };
        var tid = BlueskyPublisher.RecordKey("did:plc:authorone", local);
        Assert.Matches("^[234567abcdefghij][234567abcdefghijklmnopqrstuvwxyz]{12}$", tid);
        Assert.Equal(tid, BlueskyPublisher.RecordKey("did:plc:authorone", local));
        var remoteAt = accepted.AddMinutes(3);
        var remote = JsonSerializer.Serialize(new
        {
            uri = "at://did:plc:authorone/app.bsky.feed.post/" + tid,
            cid = "bafyremote",
            value = new { text = "The local Post", createdAt = remoteAt.ToString("O") }
        });
        var reads = 0;
        Task<HttpResponseMessage> Lookup()
        {
            reads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(remote, Encoding.UTF8, "application/json") });
        }
        var duplicate = await BlueskyPublisher.EnsureRemoteRecord(
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)), Lookup,
            "The local Post", accepted, TestContext.Current.CancellationToken);
        Assert.Equal(remoteAt, duplicate.CreatedAt);
        Assert.Equal("bafyremote", duplicate.Cid);
        var lost = await BlueskyPublisher.EnsureRemoteRecord(
            () => Task.FromException<HttpResponseMessage>(new HttpRequestException("lost response")), Lookup,
            "The local Post", accepted, TestContext.Current.CancellationToken);
        Assert.Equal(duplicate.Uri, lost.Uri);
        Assert.Equal(2, reads);
        await Assert.ThrowsAsync<InvalidOperationException>(() => BlueskyPublisher.EnsureRemoteRecord(
            () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)), Lookup,
            "Changed text", accepted, TestContext.Current.CancellationToken));
    }

    private sealed class FeedFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FeedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    [Fact]
    public async Task Topic_read_shows_current_stewardship_without_an_operation_list()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        using var member = await app.Http.GetAsync("/api/v1/experience/topics/proj-z", TestContext.Current.CancellationToken);
        member.EnsureSuccessStatusCode();
        var memberTopic = await ReadAsync(member);
        Assert.False(memberTopic.GetProperty("stewardship").GetBoolean());
        Assert.False(memberTopic.TryGetProperty("actions", out _));

        UseToken(app.Http, app.OwnerToken);
        using var owner = await app.Http.GetAsync("/api/v1/experience/topics/proj-z", TestContext.Current.CancellationToken);
        owner.EnsureSuccessStatusCode();
        var document = await ReadAsync(owner);
        Assert.True(document.GetProperty("stewardship").GetBoolean());
        Assert.False(document.TryGetProperty("actions", out _));
    }

    [Fact]
    public async Task The_ladder_runs_through_one_management_endpoint_and_refuses_the_rest()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.OwnerToken);

        // Warn: communicative, journaled as moderation, never a sanction.
        using (var warn = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.HumanParticipantId}/management",
            new { action = "warn", reason = "Please keep the tone collegial." }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, warn.StatusCode);
            var data = (await ReadAsync(warn));
            Assert.Equal("warn", data.GetProperty("action").GetString());
            Assert.Equal(app.HumanParticipantId, data.GetProperty("userRef").GetString());
        }

        // Timeout and ban cite the case that grounds them; the case names the Tangent.
        var caseRef = await ReportHumanPostAsOwner(app);
        using (var timeout = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.HumanParticipantId}/management",
            new { action = "timeout", reason = "Cool off.", durationSeconds = 3600, caseRef }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, timeout.StatusCode);
            var data = (await ReadAsync(timeout));
            Assert.True(data.GetProperty("until").GetDateTimeOffset() > DateTimeOffset.UtcNow);
            Assert.EndsWith("::proj", data.GetProperty("tangentRef").GetString());
        }
        using (var ungrounded = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.HumanParticipantId}/management",
            new { action = "timeout", reason = "No case cited.", durationSeconds = 3600 }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, ungrounded.StatusCode);
        }
        using (var timedBan = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.HumanParticipantId}/management",
            new { action = "ban", reason = "Bans are indefinite here.", durationSeconds = 60, caseRef }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, timedBan.StatusCode);
        }

        // The rung requires the permission: a member cannot climb, however politely.
        UseToken(app.Http, app.AgentToken);
        using (var member = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.HumanParticipantId}/management",
            new { action = "warn", reason = "A member has no rungs." }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, member.StatusCode);
        }
    }

    [Fact]
    public async Task An_owner_read_only_credential_cannot_manage_people_or_apply_decisions()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.OwnerToken);
        var caseRef = await ReportHumanPostAsOwner(app);
        var (credential, token) = ParticipantCredential.Issue(app.OwnerParticipantId, "Read only owner", 1,
            [ParticipationGrants.Read], DateTimeOffset.UtcNow);
        using (EntityContext.NoCache())
            await credential.Save(TestContext.Current.CancellationToken);
        UseToken(app.Http, token);
        using var arrival = await app.Http.GetAsync("/api/v1/experience", TestContext.Current.CancellationToken);
        arrival.EnsureSuccessStatusCode();
        Assert.False((await ReadAsync(arrival)).GetProperty("authority").GetProperty("canManage").GetBoolean());
        using var topic = await app.Http.GetAsync("/api/v1/experience/topics/proj-z", TestContext.Current.CancellationToken);
        topic.EnsureSuccessStatusCode();
        Assert.False((await ReadAsync(topic)).GetProperty("stewardship").GetBoolean());
        using var manage = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.HumanParticipantId}/management",
            new { action = "suspend", reason = "Must require manage grant." }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, manage.StatusCode);
        var caseId = caseRef.Split("::")[^1];
        using var decide = await app.Http.PostAsJsonAsync($"/api/v1/experience/moderation/cases/{caseId}/actions",
            new { requestId = "read-only-decision-1", action = "dismiss", summary = "Must require manage grant.",
                expectedCaseRevision = 1, expectedSubjectRevision = "unavailable" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, decide.StatusCode);
    }

    [Fact]
    public async Task Scoped_topic_steward_with_manage_grant_can_decide_while_ordinary_manager_grant_cannot()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.OwnerToken);
        var caseRef = await ReportHumanPostAsOwner(app);
        var caseId = caseRef.Split("::")[^1]["case_".Length..];
        var governance = app.Services.GetRequiredService<TopicGovernance>();
        await app.Services.GetRequiredService<TangentGovernance>().CreateTopic(app.OwnerParticipantId,
            ExperienceWebApp.TangentKey, "proj-other", "Other Topic", "Outside this stewardship scope",
            TestContext.Current.CancellationToken);
        await governance.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            app.AgentParticipantId, TopicRole.Manager, TestContext.Current.CancellationToken);
        var (stewardCredential, stewardToken) = ParticipantCredential.Issue(app.AgentParticipantId,
            "Scoped steward", 1, [ParticipationGrants.Read, ParticipationGrants.Post, ParticipationGrants.Manage],
            DateTimeOffset.UtcNow);
        var (ordinaryCredential, ordinaryToken) = ParticipantCredential.Issue(app.HumanParticipantId,
            "Ordinary participant with transport grant", 1,
            [ParticipationGrants.Read, ParticipationGrants.Post, ParticipationGrants.Manage], DateTimeOffset.UtcNow);
        using (EntityContext.NoCache())
        {
            await stewardCredential.Save(TestContext.Current.CancellationToken);
            await ordinaryCredential.Save(TestContext.Current.CancellationToken);
        }

        UseToken(app.Http, ordinaryToken);
        using var ordinaryArrival = await app.Http.GetAsync("/api/v1/experience", TestContext.Current.CancellationToken);
        ordinaryArrival.EnsureSuccessStatusCode();
        var ordinaryAuthority = (await ReadAsync(ordinaryArrival)).GetProperty("authority");
        Assert.True(ordinaryAuthority.GetProperty("canManage").GetBoolean());
        Assert.False(ordinaryAuthority.GetProperty("canStewardScope").GetBoolean());
        Assert.False(ordinaryAuthority.GetProperty("management").GetProperty("canWarn").GetBoolean());
        using var ordinaryDecision = await app.Http.PostAsJsonAsync($"/api/v1/experience/moderation/cases/{caseId}/actions",
            new { requestId = "ordinary-decision", action = "escalate", summary = "Outside my remit.",
                expectedCaseRevision = 1, expectedSubjectRevision = "unknown" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, ordinaryDecision.StatusCode);
        using var ordinaryManagement = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.AgentParticipantId}/management",
            new { action = "warn", reason = "No domain role." }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, ordinaryManagement.StatusCode);
        using var ordinaryRemoval = await app.Http.DeleteAsync("/api/v1/experience/posts/m-agent-q1",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, ordinaryRemoval.StatusCode);

        UseToken(app.Http, stewardToken);
        using var stewardArrival = await app.Http.GetAsync("/api/v1/experience", TestContext.Current.CancellationToken);
        stewardArrival.EnsureSuccessStatusCode();
        var stewardAuthority = (await ReadAsync(stewardArrival)).GetProperty("authority");
        Assert.True(stewardAuthority.GetProperty("canManage").GetBoolean());
        Assert.False(stewardAuthority.GetProperty("canStewardScope").GetBoolean());
        Assert.False(stewardAuthority.GetProperty("management").GetProperty("canWarn").GetBoolean());
        using var scopedArrival = await app.Http.GetAsync("/api/v1/experience?scopeRef=" +
            Uri.EscapeDataString(app.TopicRef), TestContext.Current.CancellationToken);
        scopedArrival.EnsureSuccessStatusCode();
        Assert.True((await ReadAsync(scopedArrival)).GetProperty("authority").GetProperty("canStewardScope").GetBoolean());
        using var siblingAccess = await app.Http.GetAsync("/api/v1/tangents/proj/topics/proj-other/access",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, siblingAccess.StatusCode);
        using var parentAccess = await app.Http.GetAsync("/api/v1/tangents/proj/access",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, parentAccess.StatusCode);
        using var topic = await app.Http.GetAsync("/api/v1/experience/topics/proj-z", TestContext.Current.CancellationToken);
        topic.EnsureSuccessStatusCode();
        Assert.True((await ReadAsync(topic)).GetProperty("stewardship").GetBoolean());
        using var read = await app.Http.GetAsync($"/api/v1/experience/moderation/cases/{caseId}",
            TestContext.Current.CancellationToken);
        read.EnsureSuccessStatusCode();
        var caseData = (await ReadAsync(read)).GetProperty("case");
        using var decision = await app.Http.PostAsJsonAsync($"/api/v1/experience/moderation/cases/{caseId}/actions",
            new { requestId = "scoped-decision", action = "escalate", summary = "Needs host attention.",
                expectedCaseRevision = caseData.GetProperty("revision").GetInt64(),
                expectedSubjectRevision = caseData.GetProperty("subjectRevision").GetString() },
            TestContext.Current.CancellationToken);
        decision.EnsureSuccessStatusCode();
        Assert.Equal("escalated", (await ReadAsync(decision)).GetProperty("case").GetProperty("state").GetString());
        using var removal = await app.Http.DeleteAsync("/api/v1/experience/posts/m-leo-r1",
            TestContext.Current.CancellationToken);
        removal.EnsureSuccessStatusCode();
        var removedRef = (await ReadAsync(removal)).GetProperty("postRef").GetString();
        Assert.EndsWith("::m-leo-r1", removedRef);
        using var again = await app.Http.DeleteAsync("/api/v1/experience/posts/m-leo-r1",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        using var history = await app.Http.GetAsync("/api/v1/experience/topics/proj-z",
            TestContext.Current.CancellationToken);
        history.EnsureSuccessStatusCode();
        Assert.True((await ReadAsync(history)).GetProperty("posts").EnumerateArray()
            .Single(post => post.GetProperty("ref").GetString() == removedRef)
            .GetProperty("removed").GetBoolean());
        await governance.SetMembership(app.OwnerParticipantId, ExperienceWebApp.TopicKey,
            app.AgentParticipantId, TopicRole.Removed, TestContext.Current.CancellationToken);
        using var revokedAccess = await app.Http.GetAsync("/api/v1/tangents/proj/topics/proj-z/access",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, revokedAccess.StatusCode);
    }

    [Fact]
    public async Task Suspend_takes_the_participant_server_wide_and_the_arrival_refuses_thereafter()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var (humanToken, _) = await IssueHumanCredential(app);
        UseToken(app.Http, app.OwnerToken);
        using var suspend = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.HumanParticipantId}/management",
            new { action = "suspend", reason = "Repeated conduct warnings." }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);

        UseToken(app.Http, humanToken);
        using var arrival = await app.Http.GetAsync("/api/v1/experience", TestContext.Current.CancellationToken);
        Assert.True(arrival.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"a suspended participant's arrival must be refused, got {(int)arrival.StatusCode}");
    }

    [Fact]
    public async Task Assign_roles_adds_and_removes_but_never_beyond_what_the_actor_holds()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.OwnerToken);
        using (var add = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.AgentParticipantId}/management",
            new { action = "add_role", reason = "Trusted with more.", role = "role:member" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        }
        using (var remove = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.AgentParticipantId}/management",
            new { action = "remove_role", reason = "Right-sizing.", role = "member" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        }
        // The derived roles are not grantable at all: everyone signed in holds them already.
        using (var derived = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.AgentParticipantId}/management",
            new { action = "add_role", reason = "Nothing to grant.", role = "role:participant" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, derived.StatusCode);
        }
        // The Owner role is not a ladder rung for anyone.
        using (var ownerRole = await app.Http.PostAsJsonAsync(
            $"/api/v1/experience/moderation/users/{app.AgentParticipantId}/management",
            new { action = "add_role", reason = "No.", role = "owner" }, TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, ownerRole.StatusCode);
        }
    }

    [Fact]
    public async Task A_thread_starts_with_one_job_replays_onto_itself_and_carries_its_opening_post()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        var body = new { requestId = "thread-replay-1", title = "Connector Threads", text = "Starting a thread from the wire." };
        using var first = await app.Http.PostAsJsonAsync("/api/v1/experience/tangents/proj/topics", body, TestContext.Current.CancellationToken);
        first.EnsureSuccessStatusCode();
        var data = (await ReadAsync(first));
        var topicRef = data.GetProperty("topicRef").GetString()!;
        var postRef = data.GetProperty("postRef").GetString()!;

        using var replay = await app.Http.PostAsJsonAsync("/api/v1/experience/tangents/proj/topics", body, TestContext.Current.CancellationToken);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(topicRef, (await ReadAsync(replay)).GetProperty("topicRef").GetString());

        using var changedTitle = await app.Http.PostAsJsonAsync("/api/v1/experience/tangents/proj/topics",
            new { requestId = "thread-replay-1", title = "A different title", text = "Starting a thread from the wire." },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, changedTitle.StatusCode);

        var window = await ReadTopic(app.Http, topicRef);
        var windowPosts = window.GetProperty("posts");
        Assert.Equal(1, windowPosts.GetArrayLength());
        Assert.Equal(postRef, windowPosts[0].GetProperty("ref").GetString());
    }

    [Fact]
    public async Task An_author_edits_and_deletes_own_posts_through_the_wire()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        var started = await app.Http.PostAsJsonAsync("/api/v1/experience/tangents/proj/topics",
            new { requestId = "thread-edit-1", title = "Edit Path", text = "Original words." }, TestContext.Current.CancellationToken);
        started.EnsureSuccessStatusCode();
        var startedData = (await ReadAsync(started));
        var topicRef = startedData.GetProperty("topicRef").GetString()!;
        var topicKey = topicRef.Split("::")[^1];
        var postRef = startedData.GetProperty("postRef").GetString()!;
        var postId = postRef.Split("::")[^1];

        using (var edit = await app.Http.PatchAsync($"/api/v1/experience/posts/{postId}",
            JsonContent.Create(new { text = "Revised words." }), TestContext.Current.CancellationToken))
        {
            edit.EnsureSuccessStatusCode();
        }
        var window = await ReadTopic(app.Http, topicRef);
        var edited = window.GetProperty("posts")[0];
        Assert.Equal("Revised words.", edited.GetProperty("text").GetString());
        Assert.NotNull(edited.GetProperty("editedAt"));

        using (var delete = await app.Http.DeleteAsync($"/api/v1/experience/posts/{postId}", TestContext.Current.CancellationToken))
        {
            delete.EnsureSuccessStatusCode();
        }
        var after = await ReadTopic(app.Http, topicRef);
        Assert.True(after.GetProperty("posts")[0].GetProperty("removed").GetBoolean());
    }

    [Fact]
    public async Task A_participant_read_answers_with_profile_and_read_filtered_activity()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        using var response = await app.Http.GetAsync(
            $"/api/v1/experience/participants/{ExperienceWebApp.HumanDid}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var data = (await ReadAsync(response));
        Assert.Equal("leo.experience.test", data.GetProperty("profile").GetProperty("handle").GetString());
        Assert.True(data.GetProperty("recentPosts").GetArrayLength() >= 1);
        Assert.All(data.GetProperty("recentPosts").EnumerateArray(), post => Assert.False(post.GetProperty("removed").GetBoolean()));
    }

    [Fact]
    public async Task Own_writes_settle_into_the_updates_feed_and_clear_on_acknowledgement()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        var started = await app.Http.PostAsJsonAsync("/api/v1/experience/tangents/proj/topics",
            new { requestId = "thread-settle-1", title = "Settlement", text = "A write that must settle." }, TestContext.Current.CancellationToken);
        started.EnsureSuccessStatusCode();
        var topicRef = (await ReadAsync(started)).GetProperty("topicRef").GetString()!;
        var topicKey = topicRef.Split("::")[^1];

        // The compiled projection appears a moment after the accepted write; the settlement
        // rides the compiled unread window, so await the compile before reading the feed.
        Assert.NotNull(await app.AwaitAttentionAsync(topicKey, app.AgentParticipantId,
            row => row.LastSequence > row.ReadSequence, TimeSpan.FromSeconds(10)));

        // Scoped to the new Topic: its opening Post is the one write awaiting settlement.
        using var updates = await app.Http.GetAsync(
            "/api/v1/experience/updates?scopeRef=" + Uri.EscapeDataString(topicRef), TestContext.Current.CancellationToken);
        updates.EnsureSuccessStatusCode();
        var items = (await ReadAsync(updates)).GetProperty("attention").GetProperty("items").EnumerateArray().ToArray();
        var settlement = Assert.Single(items, item => item.GetProperty("kind").GetString() == "write_settlement");
        Assert.Equal("A write that must settle.", settlement.GetProperty("excerpt").GetString());

        using var window = await app.Http.GetAsync($"/api/v1/experience/topics/{topicKey}", TestContext.Current.CancellationToken);
        window.EnsureSuccessStatusCode();
        var readCursor = (await ReadAsync(window)).GetProperty("readCursor").GetString()!;
        using var acknowledge = await app.Http.PostAsJsonAsync($"/api/v1/experience/topics/{topicKey}/read-position",
            new { readCursor }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, acknowledge.StatusCode);

        // The acknowledgement is also an event-time compile: wait for the row to resynchronize.
        Assert.NotNull(await app.AwaitAttentionAsync(topicKey, app.AgentParticipantId,
            row => row.LastSequence > 0 && row.ReadSequence >= row.LastSequence, TimeSpan.FromSeconds(10)));

        using var after = await app.Http.GetAsync(
            "/api/v1/experience/updates?scopeRef=" + Uri.EscapeDataString(topicRef), TestContext.Current.CancellationToken);
        after.EnsureSuccessStatusCode();
        Assert.DoesNotContain((await ReadAsync(after)).GetProperty("attention").GetProperty("items").EnumerateArray(),
            item => item.GetProperty("kind").GetString() == "write_settlement");
    }

    /// <summary>The one place a double costs something visible is the post, so the post itself
    /// carries the idempotency: the same requestId replays the row it created (ADR 0007) and a
    /// different text under it is the action's own conflict. No infrastructure, no protocol.</summary>
    [Fact]
    public async Task A_post_requestId_replays_its_row_and_conflicts_on_divergence()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        var body = new { requestId = "post-once-1", text = "Exactly once." };
        using var first = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/posts", body, TestContext.Current.CancellationToken);
        first.EnsureSuccessStatusCode();
        var postRef = (await ReadAsync(first)).GetProperty("postRef").GetString()!;

        using var replay = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/posts", body, TestContext.Current.CancellationToken);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(postRef, (await ReadAsync(replay)).GetProperty("postRef").GetString());

        using var divergent = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/posts",
            new { requestId = "post-once-1", text = "Different words, same id." }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, divergent.StatusCode);
    }

    [Fact]
    public async Task Explicit_empty_facets_survive_create_and_matching_retry()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        var body = new { requestId = "post-empty-facets-1", text = "@leo.experience.test thanks.", facets = Array.Empty<object>() };
        using var first = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/posts", body,
            TestContext.Current.CancellationToken);
        first.EnsureSuccessStatusCode();
        var postRef = (await ReadAsync(first)).GetProperty("postRef").GetString()!;
        using (EntityContext.NoCache())
        {
            var post = await Post.Get(postRef.Split("::")[^1], TestContext.Current.CancellationToken);
            Assert.NotNull(post);
            Assert.NotNull(post.Facets);
            Assert.Empty(post.Facets);
        }
        using var replay = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/posts", body,
            TestContext.Current.CancellationToken);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(postRef, (await ReadAsync(replay)).GetProperty("postRef").GetString());
    }

    [Fact]
    public async Task A_retry_repairs_a_post_saved_before_its_sequence_and_journal()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        UseToken(app.Http, app.AgentToken);
        const string requestId = "post-partial-repair-1";
        const string words = "@leo.experience.test please recover this accepted Post.";
        long sequence;
        string postId;
        using (EntityContext.NoCache())
        {
            var state = await TopicConversation.Get(ExperienceWebApp.TopicKey, TestContext.Current.CancellationToken);
            sequence = (state?.LastSequence ?? 0) + 1;
            var now = DateTimeOffset.UtcNow;
            var orphan = new Post
            {
                Id = Guid.CreateVersion7().ToString("n"), TopicKey = ExperienceWebApp.TopicKey,
                AuthorParticipantId = app.AgentParticipantId, Sequence = sequence, AcceptedAt = now,
                Content = new PostContent(words, now, null), OperationId = requestId,
                Facets = await PostFacets.Effective(words, null, TestContext.Current.CancellationToken)
            };
            postId = orphan.Id;
            await orphan.Save(TestContext.Current.CancellationToken);
            var mentioned = (await ParticipantIdentity.Get(ParticipantIdentity.AtprotoKey(ExperienceWebApp.HumanDid),
                TestContext.Current.CancellationToken))!;
            mentioned.Relabel("renamed.experience.test");
            await mentioned.Save(TestContext.Current.CancellationToken);
        }
        using var replay = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/posts",
            new { requestId, text = words }, TestContext.Current.CancellationToken);
        replay.EnsureSuccessStatusCode();
        Assert.EndsWith("::" + postId, (await ReadAsync(replay)).GetProperty("postRef").GetString());
        using (EntityContext.NoCache())
        {
            var state = await TopicConversation.Get(ExperienceWebApp.TopicKey, TestContext.Current.CancellationToken);
            Assert.True(state?.LastSequence >= sequence);
            var journal = await ActivityJournal.Query(entry => entry.Kind == ActivityKind.PostAccepted
                && entry.TopicKey == ExperienceWebApp.TopicKey && entry.PostSequence == sequence
                && entry.ActorParticipantId == app.AgentParticipantId, TestContext.Current.CancellationToken);
            Assert.Single(journal);
        }
    }

    private static async Task<JsonElement> ReadTopic(HttpClient http, string topicRef)
    {
        var topicKey = topicRef.Split("::")[^1];
        using var response = await http.GetAsync($"/api/v1/experience/topics/{topicKey}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAsync(response);
    }

    /// <summary>Files a steward-readable case on the human's post: the report is reporter-safe,
    /// so the case ref arrives through the case list, exactly like a real steward's path.</summary>
    private static async Task<string> ReportHumanPostAsOwner(ExperienceWebApp app)
    {
        using var report = await app.Http.PostAsJsonAsync("/api/v1/experience/topics/proj-z/reports",
            new { requestId = "case-ground-1", postRef = $"{app.Origin}::proj::proj-z::m-leo-r1",
                reasonCode = "conduct.tone", statement = "Inspect the tone." }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        using var cases = await app.Http.GetAsync("/api/v1/experience/topics/proj-z/moderation/cases", TestContext.Current.CancellationToken);
        cases.EnsureSuccessStatusCode();
        var list = await ReadAsync(cases);
        return list.GetProperty("cases")[0].GetProperty("caseRef").GetString()!;
    }

    private static async Task<(string Token, string CredentialId)> IssueHumanCredential(ExperienceWebApp app)
    {
        var (credential, token) = ParticipantCredential.Issue(app.HumanParticipantId, "Integration human", 1,
            [ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post], DateTimeOffset.UtcNow);
        using (EntityContext.NoCache())
            await credential.Save(TestContext.Current.CancellationToken);
        return (token, credential.Id);
    }
}
