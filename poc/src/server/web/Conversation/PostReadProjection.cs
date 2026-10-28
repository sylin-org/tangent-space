namespace Tangent.Conversation;

/// <summary>Wire projection for direct Post reads; rendered HTML never enters the stored entity.</summary>
public static class PostReadProjection
{
    public static object Of(Post post) => new
    {
        post.Id,
        post.TopicKey,
        post.AuthorParticipantId,
        post.Sequence,
        post.AcceptedAt,
        Content = new
        {
            post.Content.Text,
            post.Content.CreatedAt,
            post.Content.ReplyTo,
            post.Content.Format,
            Html = post.Removed ? null : PostMarkup.Html(post.Content),
        },
        post.Removed,
        post.RemovedAt,
        post.RemovedByParticipantId,
        post.EditedAt,
        post.Permissions,
        post.OperationId,
        post.SourceUri,
        post.SourceCid,
        post.Facets,
        post.OfPostId,
        post.PreviousChangeId,
        post.ChangeId,
    };

    public static object Of(PostPage page) => new
    {
        Messages = page.Messages.Select(Of).ToArray(),
        OpeningPost = page.OpeningPost is null ? null : Of(page.OpeningPost),
        page.NextCursor,
        page.ResumeCursor,
        page.Boundary,
        page.AuthorHandles,
        page.Resolved,
    };

    public static object Of(TopicWindow window) => new
    {
        Messages = window.Messages.Select(Of).ToArray(),
        OpeningPost = window.OpeningPost is null ? null : Of(window.OpeningPost),
        window.OlderCursor,
        window.NewerCursor,
        window.ReadCursor,
        window.Position,
        window.AuthorHandles,
        window.Resolved,
    };
}
