using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Tangent.Participants;

namespace Tangent.Connections;

/// <summary>What the network says about one Bluesky post, as the public AppView answers it: its counts and the replies
/// and quotes it shows, each a <see cref="Reference"/>; <see cref="Gone"/> where the post is no longer there.</summary>
public sealed record Heard(int Likes, int Reposts, int Quotes, int Replies, IReadOnlyList<Reference> References, bool Gone = false)
{
    public static Heard Nothing { get; } = new(0, 0, 0, 0, [], Gone: true);
}

/// <summary>The public reads of what the network says about a published article's Bluesky post: its thread one level
/// deep and its quotes, from the public atproto service the profile reads use (<see cref="ProfileReaderOptions"/>),
/// without a session, bounded in time and bytes. They run in the <see cref="Elsewhere"/> job, never in a request.</summary>
public sealed class AppView(HttpClient http, IOptions<ProfileReaderOptions> options)
{
    /// <summary>The largest answer read.</summary>
    public const int MaximumResponseBytes = 512 * 1024;

    /// <summary>The most replies and quotes each read asks for.</summary>
    public const int Limit = 20;

    /// <summary>The longest text a reference keeps.</summary>
    public const int TextLength = 300;

    public async Task<Heard> Read(string note, CancellationToken ct)
    {
        using var thread = await Get("app.bsky.feed.getPostThread?depth=1&parentHeight=0&uri=" + Uri.EscapeDataString(note), ct);
        if (thread is null || !thread.RootElement.TryGetProperty("thread", out var view) || !view.TryGetProperty("post", out var post))
            return Heard.Nothing;

        var references = new List<Reference>();
        if (view.TryGetProperty("replies", out var replies) && replies.ValueKind == JsonValueKind.Array)
            foreach (var reply in replies.EnumerateArray().Take(Limit))
                if (reply.TryGetProperty("post", out var replied) && Of(Reference.Reply, replied) is { } reference) references.Add(reference);

        var quotes = Count(post, "quoteCount");
        if (quotes > 0)
        {
            using var quoting = await Get("app.bsky.feed.getQuotes?limit=" + Limit + "&uri=" + Uri.EscapeDataString(note), ct);
            if (quoting is not null && quoting.RootElement.TryGetProperty("posts", out var posts) && posts.ValueKind == JsonValueKind.Array)
                foreach (var quote in posts.EnumerateArray().Take(Limit))
                    if (Of(Reference.Quote, quote) is { } reference) references.Add(reference);
        }

        return new Heard(Count(post, "likeCount"), Count(post, "repostCount"), quotes, Count(post, "replyCount"),
            references.OrderByDescending(reference => reference.At).Take(Limit).ToList());
    }

    /// <summary>One read, or null where the service says the post is not there.</summary>
    private async Task<JsonDocument?> Get(string query, CancellationToken ct)
    {
        var settings = options.Value;
        var baseAddress = settings.BaseAddress.AbsoluteUri.TrimEnd('/');
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(settings.Timeout);
        using var response = await http.GetAsync(baseAddress + "/xrpc/" + query, bounded.Token);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(bounded.Token), cancellationToken: bounded.Token);
    }

    /// <summary>A post view as a reference, attributed to its author and linked to where it is read.</summary>
    private static Reference? Of(string kind, JsonElement post)
    {
        if (Text(post, "uri") is not { } uri || Publishing.Bluesky(uri) is not { } address
            || !post.TryGetProperty("author", out var author) || Text(author, "did") is not { } did) return null;
        var record = post.TryGetProperty("record", out var held) ? held : default;
        var text = Text(record, "text") ?? "";
        var at = DateTimeOffset.TryParse(Text(record, "createdAt") ?? Text(post, "indexedAt"), out var written) ? written : DateTimeOffset.UnixEpoch;
        return new Reference(kind, did, Text(author, "handle") ?? did, Text(author, "displayName"),
            text.Length <= TextLength ? text : text[..(TextLength - 1)].TrimEnd() + "…", at, address);
    }

    private static int Count(JsonElement post, string name)
        => post.TryGetProperty(name, out var count) && count.TryGetInt32(out var value) ? Math.Max(0, value) : 0;

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var field)
           && field.ValueKind == JsonValueKind.String && field.GetString() is { Length: > 0 } text ? text : null;
}
