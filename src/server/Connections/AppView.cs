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
/// without a session, bounded in time and bytes. They run in the <see cref="Elsewhere"/> job, never in a request. What
/// the article's author hid there stays out here — a reply its post's gate lists, a quote they detached — and so does
/// what Bluesky labels to hide or take down, and every post of an account that asks apps to show it to no one signed
/// out, for every reader here alike.</summary>
public sealed class AppView(HttpClient http, IOptions<ProfileReaderOptions> options)
{
    /// <summary>The largest answer read.</summary>
    public const int MaximumResponseBytes = 512 * 1024;

    /// <summary>The most replies and quotes each read asks for.</summary>
    public const int Limit = 20;

    /// <summary>The longest text a reference keeps.</summary>
    public const int TextLength = 300;

    /// <summary>The labels under which Bluesky shows a post to no one, nor any post of a labelled account.</summary>
    private static readonly HashSet<string> Hiding = new(["!hide", "!takedown"], StringComparer.Ordinal);

    /// <summary>The labels under which an account's posts stay out here too: those that hide, and the one an account sets
    /// on itself asking apps to show its posts to no one signed out, which a page here may be read as.</summary>
    private static readonly HashSet<string> HidingAccount = new([.. Hiding, "!no-unauthenticated"], StringComparer.Ordinal);

    public async Task<Heard> Read(string note, CancellationToken ct)
    {
        using var thread = await Get("app.bsky.feed.getPostThread?depth=1&parentHeight=0&uri=" + Uri.EscapeDataString(note), ct);
        if (thread is null || !thread.RootElement.TryGetProperty("thread", out var view) || !view.TryGetProperty("post", out var post))
            return Heard.Nothing;

        var hidden = Strings(Field(Field(Field(thread.RootElement, "threadgate"), "record"), "hiddenReplies"));
        var references = Items(Field(view, "replies"))
            .Select(reply => Field(reply, "post"))
            .Where(replied => !hidden.Contains(Text(replied, "uri") ?? ""))
            .Select(replied => Of(Reference.Reply, replied)).OfType<Reference>().Take(Limit).ToList();

        var quotes = Count(post, "quoteCount");
        if (quotes > 0)
        {
            using var quoting = await Get("app.bsky.feed.getQuotes?limit=" + Limit + "&uri=" + Uri.EscapeDataString(note), ct);
            if (quoting is not null)
                references.AddRange(Items(Field(quoting.RootElement, "posts"))
                    .Where(quote => !Detached(quote))
                    .Select(quote => Of(Reference.Quote, quote)).OfType<Reference>().Take(Limit));
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

    /// <summary>A post view as a reference, attributed to its author and linked to where it is read; none where a label on
    /// it or on its author hides it, or its author asks to be shown to no one signed out.</summary>
    private static Reference? Of(string kind, JsonElement post)
    {
        if (Text(post, "uri") is not { } uri || Publishing.Bluesky(uri) is not { } address
            || Field(post, "author") is not { ValueKind: JsonValueKind.Object } author || Text(author, "did") is not { } did
            || Labelled(post, Hiding) || Labelled(author, HidingAccount)) return null;
        var record = post.TryGetProperty("record", out var held) ? held : default;
        var text = Text(record, "text") ?? "";
        var at = DateTimeOffset.TryParse(Text(record, "createdAt") ?? Text(post, "indexedAt"), out var written) ? written : DateTimeOffset.UnixEpoch;
        return new Reference(kind, did, Text(author, "handle") ?? did, Text(author, "displayName"),
            text.Length <= TextLength ? text : text[..(TextLength - 1)].TrimEnd() + "…", at, address);
    }

    /// <summary>Whether a label on a post or an account, not negated, is one of <paramref name="values"/>.</summary>
    private static bool Labelled(JsonElement labelled, IReadOnlySet<string> values)
        => Items(Field(labelled, "labels")).Any(label => values.Contains(Text(label, "val") ?? "") && Field(label, "neg").ValueKind != JsonValueKind.True);

    /// <summary>Whether the quote's embed of the post it quotes is detached, which that post's author does on Bluesky: in a
    /// record embed, or in the record of a record embed with media.</summary>
    private static bool Detached(JsonElement quote)
    {
        var embedded = Field(Field(quote, "embed"), "record");
        return Field(embedded, "detached").ValueKind == JsonValueKind.True || Field(Field(embedded, "record"), "detached").ValueKind == JsonValueKind.True;
    }

    private static JsonElement Field(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var field) ? field : default;

    private static IEnumerable<JsonElement> Items(JsonElement list)
        => list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static HashSet<string> Strings(JsonElement list)
        => new(Items(list).Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!), StringComparer.Ordinal);

    private static int Count(JsonElement post, string name)
        => post.TryGetProperty(name, out var count) && count.TryGetInt32(out var value) ? Math.Max(0, value) : 0;

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var field)
           && field.ValueKind == JsonValueKind.String && field.GetString() is { Length: > 0 } text ? text : null;
}
