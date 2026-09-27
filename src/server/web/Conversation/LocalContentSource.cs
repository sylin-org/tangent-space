using Tangent.Api;

namespace Tangent.Conversation;

/// <summary>Local Topic content through the same bounded, current-policy public reader used
/// by web documents. Authenticated Topic reads remain in ConversationService.</summary>
public sealed class LocalContentSource(PublicConversationReader reader) : IContentSource
{
    public string Key => "local";

    public async Task<ContentPage?> Read(ContentQuery query, CancellationToken ct)
    {
        if (query.TangentKey is null || query.TopicKey is null)
            throw new ArgumentException("Choose a Tangent and Topic for local content.");
        var window = await reader.ReadTopic(query.TangentKey, query.TopicKey, query.Before, query.After,
            query.Limit, ct);
        return window is null ? null : new ContentPage(Key,
            window.Posts.Select(post => new ContentItem(Key, post.Id, post.Author.Label,
                post.Text ?? "", post.CreatedAt, post.Path)).ToArray(), window.OlderBefore, window.NewerAfter);
    }
}
