using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;

namespace Tangent.Conversation;

/// <summary>Reads selected public authors from the Bluesky AppView into a durable Tangent
/// projection. No participant credential or remote write is used.</summary>
public sealed class BlueskyContentSource(IHttpClientFactory clients, TimeProvider clock) : IContentSource
{
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private const int MaximumPosts = 25;
    public string Key => "bluesky";

    public async Task<IReadOnlyList<BlueskyAuthorSummary>> Authors(int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 25) throw new ArgumentException("Choose 1 to 25 authors.");
        var member = typeof(BlueskySourceState).GetProperty(nameof(BlueskySourceState.RefreshedAt))!;
        using var fresh = EntityContext.NoCache();
        var states = await BlueskySourceState.Query(_ => true, new QueryDefinition
        {
            Page = 1, PageSize = limit,
            Sort = [new SortSpec(new MemberPath(typeof(BlueskySourceState), [member], typeof(DateTimeOffset), false, -1), true)]
        }, ct);
        return states.Select(state => new BlueskyAuthorSummary(state.Id, state.Handle, state.RefreshedAt,
            "/sources/bluesky/" + Uri.EscapeDataString(state.Id))).ToArray();
    }

    public async Task<ContentPage?> Read(ContentQuery query, CancellationToken ct)
    {
        var actor = ActorDid(query.ActorDid);
        if (query.Limit is < 1 or > MaximumPosts) throw new ArgumentException("Choose 1 to 25 Posts.");
        if (query.Before is not null && query.After is not null)
            throw new ArgumentException("Choose before or after, not both.");
        if (await BlueskySourceState.Get(actor, ct) is null) return null;
        using var fresh = EntityContext.NoCache();
        var before = await Edge(actor, query.Before, ct);
        var after = await Edge(actor, query.After, ct);
        var descending = before is not null || after is null;
        var definition = BySequence(query.Limit, descending);
        var rows = before is { } older
            ? await BlueskyProjection.Query(post => post.ActorDid == actor && !post.Hidden && post.Sequence < older, definition, ct)
            : after is { } newer
                ? await BlueskyProjection.Query(post => post.ActorDid == actor && !post.Hidden && post.Sequence > newer, definition, ct)
                : await BlueskyProjection.Query(post => post.ActorDid == actor && !post.Hidden, definition, ct);
        var selected = rows.OrderBy(post => post.Sequence).ToArray();
        string? olderBefore = null, newerAfter = null;
        if (selected.Length > 0)
        {
            var first = selected[0];
            var last = selected[^1];
            if ((await BlueskyProjection.Query(post => post.ActorDid == actor && !post.Hidden && post.Sequence < first.Sequence,
                    BySequence(1, true), ct)).Count > 0) olderBefore = first.Id;
            if ((await BlueskyProjection.Query(post => post.ActorDid == actor && !post.Hidden && post.Sequence > last.Sequence,
                    BySequence(1, false), ct)).Count > 0) newerAfter = last.Id;
        }
        return new ContentPage(Key, selected.Select(post => new ContentItem(Key, post.Id,
            post.AuthorHandle.Length > 0 ? post.AuthorHandle : post.ActorDid,
            post.Text, post.CreatedAt, PostUrl(post), post.SourceUri, post.SourceCid)).ToArray(),
            olderBefore, newerAfter);
    }

    public async Task<BlueskyRefreshResult> Refresh(string actorDid, CancellationToken ct)
    {
        var actor = await ResolveDid(actorDid, ct);
        await refreshGate.WaitAsync(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            var state = await BlueskySourceState.Get(actor, ct) ?? new BlueskySourceState { Id = actor };
            var imported = 0;
            var hidden = 0;
            if (state.ReconcilePending)
            {
                hidden = await HideStale(actor, state.SweepStartedAt, ct);
                state.ReconcilePending = await HasStale(actor, state.SweepStartedAt, ct);
                if (!state.ReconcilePending) state.SweepStartedAt = default;
            }
            else
            {
                if (state.SweepStartedAt == default) state.SweepStartedAt = clock.GetUtcNow();
                var window = await Fetch(actor, state.SweepCursor, ct);
                imported = await Store(actor, window.Posts, fromFeed: true, ct);
                state.Handle = window.Posts.FirstOrDefault()?.Handle ?? state.Handle;
                state.SweepCursor = window.Cursor;
                if (window.Cursor is null)
                {
                    hidden = await HideStale(actor, state.SweepStartedAt, ct);
                    state.ReconcilePending = await HasStale(actor, state.SweepStartedAt, ct);
                    if (!state.ReconcilePending) state.SweepStartedAt = default;
                }
            }
            state.RefreshedAt = clock.GetUtcNow();
            await state.Save(ct);
            return new BlueskyRefreshResult(actor, imported, state.RefreshedAt,
                state.ReconcilePending ? "reconcile_available"
                : state.SweepStartedAt != default ? "sweep_available" : "complete", "disconnected", hidden);
        }
        finally { refreshGate.Release(); }
    }

    public async Task ProjectPublished(string actorDid, string uri, string cid, string text,
        DateTimeOffset createdAt, CancellationToken ct)
    {
        var actor = ActorDid(actorDid);
        await refreshGate.WaitAsync(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            if (await BlueskyProjection.Get(StableId(uri), ct) is null)
                await Store(actor, [new RemotePost(uri, cid, "", text, null, createdAt)], fromFeed: false, ct);
            if (await BlueskySourceState.Get(actor, ct) is null)
                await new BlueskySourceState { Id = actor }.Save(ct);
        }
        finally { refreshGate.Release(); }
    }

    private async Task<string> ResolveDid(string actor, CancellationToken ct)
    {
        if (actor.StartsWith("did:", StringComparison.Ordinal)) return ActorDid(actor);
        if (actor.Length is < 3 or > 253 || actor.Split('.').Length < 2
            || actor.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-')))
            throw new ArgumentException("Choose a Bluesky handle or actor DID.");
        using var response = await clients.CreateClient("bluesky-content").GetAsync(
            "/xrpc/app.bsky.actor.getProfile?actor=" + Uri.EscapeDataString(actor),
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(100_000, ct);
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var profile = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        var did = profile.RootElement.TryGetProperty("did", out var value) ? value.GetString() : null;
        return ActorDid(did);
    }

    private async Task<int> Store(string actor, IReadOnlyList<RemotePost> posts, bool fromFeed, CancellationToken ct)
    {
        var count = 0;
        foreach (var remote in posts)
        {
            var id = StableId(remote.Uri);
            var post = await BlueskyProjection.Get(id, ct);
            if (post is null)
            {
                post = new BlueskyProjection
                {
                    Id = id, ActorDid = actor, SourceUri = remote.Uri,
                    Sequence = await AllocateSequence(actor, remote, ct)
                };
                count++;
            }
            post.AuthorHandle = remote.Handle.Length > 0 ? remote.Handle : post.AuthorHandle;
            post.SourceCid = remote.Cid;
            post.Text = remote.Text;
            post.ReplyToUri = remote.ReplyToUri;
            post.CreatedAt = remote.CreatedAt;
            post.FetchedAt = clock.GetUtcNow();
            post.SeenInFeed |= fromFeed;
            post.Hidden = false;
            await post.Save(ct);
        }
        return count;
    }

    private static async Task<long> AllocateSequence(string actor, RemotePost remote, CancellationToken ct)
    {
        var candidate = SortKey(remote.CreatedAt, remote.Uri);
        // The timestamp/hash key is normally unique, but its 20-bit suffix can
        // collide. Reserve a free adjacent value before saving, while refreshGate
        // serializes this actor's inserts, so strict < and > paging remains total.
        for (var probe = 0; probe < 1024; probe++, candidate = checked(candidate + 1))
        {
            var occupied = await BlueskyProjection.Query(post => post.ActorDid == actor
                && post.Sequence == candidate, BySequence(1, false), ct);
            if (occupied.Count == 0) return candidate;
        }
        throw new InvalidOperationException("Bluesky Post ordering space is exhausted for this timestamp.");
    }

    private async Task<int> HideStale(string actor, DateTimeOffset sweepStarted, CancellationToken ct)
    {
        // Give a freshly published PDS record time to reach the AppView before using
        // a completed author-feed sweep as evidence that it is no longer visible.
        var publicationGrace = clock.GetUtcNow().AddHours(-1);
        var stale = await BlueskyProjection.Query(post => post.ActorDid == actor
            && (post.SeenInFeed || post.FetchedAt < publicationGrace)
            && !post.Hidden && post.FetchedAt < sweepStarted, BySequence(MaximumPosts, false), ct);
        foreach (var post in stale)
        {
            post.Hidden = true;
            await post.Save(ct);
        }
        return stale.Count;
    }

    private async Task<bool> HasStale(string actor, DateTimeOffset sweepStarted, CancellationToken ct)
    {
        var publicationGrace = clock.GetUtcNow().AddHours(-1);
        return (await BlueskyProjection.Query(post => post.ActorDid == actor
            && (post.SeenInFeed || post.FetchedAt < publicationGrace)
            && !post.Hidden && post.FetchedAt < sweepStarted, BySequence(1, false), ct)).Count > 0;
    }

    private async Task<(IReadOnlyList<RemotePost> Posts, string? Cursor)> Fetch(string actor, string? cursor, CancellationToken ct)
    {
        var path = "/xrpc/app.bsky.feed.getAuthorFeed?actor=" + Uri.EscapeDataString(actor)
            + "&limit=" + MaximumPosts + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
        using var response = await clients.CreateClient("bluesky-content").GetAsync(path,
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1_000_000, ct);
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: ct);
        var root = document.RootElement;
        var result = new List<RemotePost>();
        if (root.TryGetProperty("feed", out var feed) && feed.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in feed.EnumerateArray())
            {
                if (!item.TryGetProperty("post", out var view)
                    || !view.TryGetProperty("author", out var author)
                    || !author.TryGetProperty("did", out var did)
                    || !string.Equals(did.GetString(), actor, StringComparison.Ordinal)
                    || !view.TryGetProperty("record", out var record)
                    || !record.TryGetProperty("text", out var text)
                    || !view.TryGetProperty("uri", out var uri)
                    || !view.TryGetProperty("cid", out var cid)) continue;
                var created = record.TryGetProperty("createdAt", out var timestamp)
                    && DateTimeOffset.TryParse(timestamp.GetString(), out var parsed) ? parsed : clock.GetUtcNow();
                var reply = record.TryGetProperty("reply", out var relation)
                    && relation.TryGetProperty("parent", out var parent)
                    && parent.TryGetProperty("uri", out var parentUri) ? parentUri.GetString() : null;
                result.Add(new RemotePost(uri.GetString()!, cid.GetString()!,
                    author.TryGetProperty("handle", out var handle) ? handle.GetString() ?? "" : "",
                    text.GetString() ?? "", reply, created));
            }
        }
        var next = root.TryGetProperty("cursor", out var edge) && edge.ValueKind == JsonValueKind.String
            ? edge.GetString() : null;
        return (result, next);
    }

    private static string ActorDid(string? actor)
    {
        if (actor is null || actor.Length is < 10 or > 253
            || !(actor.StartsWith("did:plc:", StringComparison.Ordinal)
                 || actor.StartsWith("did:web:", StringComparison.Ordinal))
            || actor.Any(char.IsWhiteSpace)) throw new ArgumentException("Choose a Bluesky actor DID.");
        return actor;
    }

    private static async Task<long?> Edge(string actor, string? id, CancellationToken ct)
    {
        if (id is null) return null;
        if (id.Length != 64) throw new ArgumentException("Choose a Post from this Bluesky source.");
        var post = await BlueskyProjection.Get(id, ct);
        if (post is null || post.ActorDid != actor || post.Hidden)
            throw new ArgumentException("Choose a Post from this Bluesky source.");
        return post.Sequence;
    }

    private static QueryDefinition BySequence(int size, bool descending)
    {
        var member = typeof(BlueskyProjection).GetProperty(nameof(BlueskyProjection.Sequence))!;
        return new QueryDefinition { Page = 1, PageSize = size,
            Sort = [new SortSpec(new MemberPath(typeof(BlueskyProjection), [member], typeof(long), false, -1), descending)] };
    }

    private static string StableId(string uri)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri)));

    internal static long SortKey(DateTimeOffset created, string uri)
    {
        var milliseconds = Math.Clamp(created.ToUnixTimeMilliseconds(), 0, long.MaxValue / 1_048_576);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(uri));
        return milliseconds * 1_048_576 + (BitConverter.ToUInt32(hash) & 0xfffff);
    }

    private static string PostUrl(BlueskyProjection post)
    {
        var rkey = post.SourceUri.Split('/').LastOrDefault() ?? "";
        return "https://bsky.app/profile/" + Uri.EscapeDataString(post.ActorDid) + "/post/" + Uri.EscapeDataString(rkey);
    }

    private sealed record RemotePost(string Uri, string Cid, string Handle, string Text,
        string? ReplyToUri, DateTimeOffset CreatedAt);
}

public sealed record BlueskyRefreshResult(string ActorDid, int Imported, DateTimeOffset RefreshedAt,
    string Backfill, string Publish, int Hidden = 0);

public sealed record BlueskyAuthorSummary(string ActorDid, string Handle, DateTimeOffset RefreshedAt,
    string PostsUrl);
