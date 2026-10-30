using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tangent.Render;

/// <summary>A <see cref="Feed"/> as a JSON Feed document (version 1.1).</summary>
public static class JsonFeed
{
    public const string MediaType = "application/feed+json";

    public const string Extension = ".json";

    private const string Version1_1 = "https://jsonfeed.org/version/1.1";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Of(Feed feed) => JsonSerializer.Serialize(new Document(
        Version1_1,
        feed.Title,
        feed.Home,
        feed.Address + Extension,
        feed.Description.Length > 0 ? feed.Description : null,
        feed.Entries.Select(entry => new Item(entry.Address, entry.Address, entry.Title, entry.Html, entry.Published, entry.Updated,
            [new Author(entry.Author, entry.AuthorAddress)])).ToList()), Options) + "\n";

    private sealed record Document(string Version, string Title, string HomePageUrl, string FeedUrl, string? Description, IReadOnlyList<Item> Items);

    private sealed record Item(string Id, string Url, string Title, string ContentHtml, DateTimeOffset DatePublished, DateTimeOffset DateModified,
        IReadOnlyList<Author> Authors);

    private sealed record Author(string Name, string? Url);
}
