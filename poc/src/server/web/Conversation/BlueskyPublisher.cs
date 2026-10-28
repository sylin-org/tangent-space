using System.Globalization;
using System.Buffers.Binary;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Tangent.Api;
using Tangent.Application;
using Tangent.Community;
using Tangent.Identity;

namespace Tangent.Conversation;

/// <summary>Publishes an explicitly selected, currently public local Post to its author's
/// Bluesky repository after separate account consent. A deterministic remote record key makes
/// a retry after a lost response converge on the same record.</summary>
public sealed class BlueskyPublisher(AtprotoSessions sessions, BlueskyContentSource source,
    ConversationService conversation, PolicyGate acceptance, TimeProvider clock, References references)
{
    public const string WriteScope = "repo:app.bsky.feed.post";
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<BlueskyPublishResult> Publish(ClaimsPrincipal principal, string postId,
        string requestId, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Post);
        var did = principal.FindFirst(AtprotoClaimTypes.Did)?.Value;
        if (did is null) throw new UnauthorizedAccessException("Connect a Bluesky account to publish.");
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128)
            throw new ArgumentException("Provide a stable request id of at most 128 characters.");
        // Consent/refresh may resolve the account over the network. Do that before
        // entering the policy gate, then make the final local decision inside it.
        var metadata = await sessions.GetSessionMetadata(did, ct);
        if (metadata is null || !metadata.Scopes.Contains(WriteScope, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Connect Bluesky with Post publishing access first.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var publishCt = deadline.Token;
        await gate.WaitAsync(publishCt);
        try
        {
            // All local privacy, restriction and Post mutations enter this same gate.
            // No database transaction is held while the bounded PDS request is sent.
            await acceptance.Enter(publishCt);
            try
            {
            Post post;
            using (EntityContext.NoCache())
            {
                post = await Post.Get(postId, publishCt)
                    ?? throw new ArgumentException("Choose a local Post to publish.");
                if (post.AuthorParticipantId != participantId || post.Removed || post.OfPostId is not null)
                    throw new UnauthorizedAccessException("Publish one of your available Posts.");
                var policy = await conversation.ReadPolicy(participantId, post.TopicKey, publishCt);
                if (!policy.CanWrite)
                    throw new UnauthorizedAccessException("Current Topic rules do not allow publishing this Post.");
                var topic = await Topic.Get(post.TopicKey, publishCt);
                var tangent = topic is null ? null : await Community.Tangent.Get(topic.TangentKey, publishCt);
                if (topic is null || !PublicTopicAccess.IsPublic(topic, tangent))
                    throw new UnauthorizedAccessException("Make the Topic publicly readable before publishing this Post.");
            }
            var topicForPreview = await Topic.Get(post.TopicKey, publishCt);
            var articleUrl = sourceUrl(topicForPreview!, post);
            var tangentForPreview = topicForPreview is null ? null : await Community.Tangent.Get(topicForPreview.TangentKey, publishCt);
            var text = tangentForPreview?.Kind == TangentKind.Articles
                ? topicForPreview!.Title + "\n\n" + articleUrl : PostMarkup.PlainText(post.Content);
            if (new StringInfo(text).LengthInTextElements > 300 || Encoding.UTF8.GetByteCount(text) > 3000)
                throw new ArgumentException("Bluesky Posts must fit within 300 characters and 3000 UTF-8 bytes.");
            var digest = Hash(text);
            // A Post has one remote identity even when a caller accidentally changes its
            // retry id. This also survives a successful remote write followed by a failed
            // local receipt save: the next attempt probes the same remote record key.
            var key = Hash(did + "\n" + postId);
            var existing = await BlueskyPublication.Get(key, publishCt);
            if (existing is not null)
            {
                if (existing.LocalPostId != postId || existing.TextDigest != digest)
                    throw new InvalidOperationException("This request id already published different content.");
                await source.ProjectPublished(did, existing.SourceUri, existing.SourceCid,
                    text, existing.PublishedAt, publishCt);
                return new BlueskyPublishResult(existing.SourceUri, existing.SourceCid,
                    existing.PublishedAt, Replayed: true);
            }
            // app.bsky.feed.post requires a TID rkey. Derive a stable, valid TID from
            // the accepted local Post and actor so retries do not mint another record.
            var rkey = RecordKey(did, post);
            var createdAt = clock.GetUtcNow();
            var record = new Dictionary<string, object>
            {
                ["$type"] = "app.bsky.feed.post", ["text"] = text,
                ["createdAt"] = createdAt.ToString("O")
            };
            using var body = new StringContent(JsonSerializer.Serialize(new
            {
                repo = did, collection = "app.bsky.feed.post", rkey, record
            }), Encoding.UTF8, "application/json");
            var (uri, cid, publishedAt) = await EnsureRemoteRecord(
                () => sessions.Send(did, "com.atproto.repo.createRecord", HttpMethod.Post,
                    content: body, ct: publishCt),
                () => sessions.Send(did, "com.atproto.repo.getRecord", HttpMethod.Get,
                    [new KeyValuePair<string, string>("repo", did),
                     new KeyValuePair<string, string>("collection", "app.bsky.feed.post"),
                     new KeyValuePair<string, string>("rkey", rkey)], ct: publishCt),
                text, createdAt, publishCt);
            var publication = new BlueskyPublication
            {
                Id = key, ActorDid = did, LocalPostId = postId, FirstRequestId = requestId,
                TextDigest = digest,
                SourceUri = uri, SourceCid = cid, PublishedAt = publishedAt
            };
            await publication.Save(publishCt);
            await source.ProjectPublished(did, uri, cid, text, publishedAt, publishCt);
            return new BlueskyPublishResult(uri, cid, publishedAt, Replayed: false);
            }
            finally { acceptance.Exit(); }
        }
        finally { gate.Release(); }
    }

    private string sourceUrl(Topic topic, Post post) => references.Origin + "/t/" + Uri.EscapeDataString(topic.TangentKey) + "/topics/" + Uri.EscapeDataString(topic.Id);

    internal static async Task<(string Uri, string Cid, DateTimeOffset CreatedAt)> EnsureRemoteRecord(
        Func<Task<HttpResponseMessage>> create, Func<Task<HttpResponseMessage>> get,
        string text, DateTimeOffset createdAt, CancellationToken ct)
    {
        HttpResponseMessage? created = null;
        try { created = await create(); }
        catch (HttpRequestException) { /* The PDS may have accepted the write before the response was lost. */ }
        if (created is not null)
        {
            using (created)
            {
                if (created.IsSuccessStatusCode)
                {
                    var (uri, cid) = await Created(created, ct);
                    return (uri, cid, createdAt);
                }
            }
        }
        using var response = await get();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("The Bluesky publication could not be confirmed.");
        using var document = await ReadBody(response, ct);
        var root = document.RootElement;
        var remoteText = root.GetProperty("value").GetProperty("text").GetString();
        if (remoteText != text)
            throw new InvalidOperationException("This Post already names different content on Bluesky.");
        var remoteTimestamp = root.GetProperty("value").GetProperty("createdAt").GetString();
        if (!DateTimeOffset.TryParse(remoteTimestamp, out var remoteCreatedAt))
            throw new HttpRequestException("Bluesky returned an invalid Post timestamp.");
        return (root.GetProperty("uri").GetString()!, root.GetProperty("cid").GetString()!, remoteCreatedAt);
    }

    private static async Task<(string Uri, string Cid)> Created(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("Bluesky did not accept the Post.");
        using var document = await ReadBody(response, ct);
        var root = document.RootElement;
        return (root.GetProperty("uri").GetString()!, root.GetProperty("cid").GetString()!);
    }

    private static async Task<JsonDocument> ReadBody(HttpResponseMessage response, CancellationToken ct)
    {
        await response.Content.LoadIntoBufferAsync(100_000, ct);
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(body, cancellationToken: ct);
    }

    private static string Hash(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static string RecordKey(string did, Post post)
    {
        const string alphabet = "234567abcdefghijklmnopqrstuvwxyz";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(did + "\n" + post.Id));
        var entropy = BinaryPrimitives.ReadUInt32BigEndian(hash);
        var micros = checked(post.AcceptedAt.ToUnixTimeMilliseconds() * 1000 + entropy % 1000);
        if (micros <= 0 || micros >= 1L << 53)
            throw new ArgumentException("This Post has an invalid acceptance timestamp for Bluesky.");
        var value = ((ulong)micros << 10) | (ulong)((entropy >> 10) & 1023);
        Span<char> tid = stackalloc char[13];
        for (var index = tid.Length - 1; index >= 0; index--)
        {
            tid[index] = alphabet[(int)(value & 31)];
            value >>= 5;
        }
        return new string(tid);
    }
}

public sealed record BlueskyPublishResult(string Uri, string Cid, DateTimeOffset PublishedAt, bool Replayed);
