using Tangent.Posts;
using Tangent.Render;

namespace Tangent.Connections;

/// <summary>Where an article stands on its author's own account, as its page tells its author: the state's word, its
/// sentence, the form that publishes it where they may, the form that says not now to the first offer, the article's
/// Bluesky post once it is out, and their Connections page. <see cref="Document"/> is the article's document record, which
/// the page's head names for everyone once it is out.</summary>
public sealed record Publishing(string State, string Sentence, string? Detail, Form? Publish, Form? NotNow, string? Elsewhere, string Connections)
{
    public const string Offer = "offer";

    /// <summary>The article's line for <paramref name="readerId"/> and its document, from two keyed reads: the receipt of
    /// its author's Connection and, for its author, that Connection. A reader other than the author, an article that is not
    /// public or is removed, and an author who said not now read no line.</summary>
    public static async Task<(Publishing? Line, string? Document)> For(Post root, string? readerId, string? handle, bool open,
        string articleAddress, string baseAddress, CancellationToken ct)
    {
        if (root.Kind != PostKind.Article || root.ParentId is not null) return (null, null);
        var receipt = await Receipt.Get(Receipt.Key(root.AuthorId, root.Id), ct);
        var document = receipt?.IsLive() == true && open && !root.Removed ? receipt.Document : null;
        if (readerId != root.AuthorId || !open || root.Removed) return (null, document);

        var connection = await Connection.Get(readerId, ct);
        var connections = baseAddress + ConnectionsController.Path;
        var publish = Form.Posting(articleAddress + "/" + PublishController.Segment);
        if (connection is null)
            return (new Publishing(Offer, "Publish this on Bluesky as you?",
                "It goes out under your own account" + (handle is null ? "" : ", @" + handle) + ", and follows your edits and removal.",
                publish, Form.Posting(baseAddress + PublishController.NotNowPath, new Form.Field(PublishController.BackField, "The page to come back to.",
                    PublishController.Back(articleAddress, baseAddress))), null, connections), document);
        if (connection.State == ConnectionState.NotNow) return (null, document);
        if (receipt is null || receipt.Retracted)
            return (connection.NeedsGrant
                ? new Publishing("grant", Syndication.Ungranted, null, publish, null, null, connections)
                : new Publishing(Offer, "Publish this on Bluesky too?", null, publish, null, null, connections), document);

        var state = Receipt.StateOf(receipt, root, connection);
        var elsewhere = receipt.Note is { } note ? Bluesky(note) : null;
        return (state switch
        {
            ArticleState.Publishing => new Publishing("publishing", "Publishing on Bluesky as you…", null, null, null, null, connections),
            ArticleState.Updating => new Publishing("updating", "Updating it on Bluesky…", null, null, null, elsewhere, connections),
            ArticleState.Trying => new Publishing("trying", connection.Trouble ?? Syndication.Unreachable, null, null, null, elsewhere, connections),
            ArticleState.NeedsGrant => new Publishing("grant", Syndication.Ungranted, null, publish, null, elsewhere, connections),
            ArticleState.Updated => new Publishing("updated", "Updated on Bluesky.", null, null, null, elsewhere, connections),
            _ => new Publishing("published", "Published on Bluesky as you.", null, null, null, elsewhere, connections)
        }, document);
    }

    /// <summary>Whether a visitor reads the Tangent <paramref name="tangentId"/> of a place of <paramref name="space"/>: the
    /// place is public and the Tangent's own audience, where it states one, is not private. Only such an article is
    /// published.</summary>
    public static async Task<bool> Open(Space.TangentSpace space, string tangentId, CancellationToken ct)
        => space.Audience == Space.Audience.Public && await Tangents.Overwrites.AudienceOf(tangentId, ct) != Space.Audience.Private;

    /// <summary>Where a Bluesky post is read, from its <c>at://</c> address.</summary>
    public static string? Bluesky(string note)
        => note.Split('/') is ["at:", "", var did, AtprotoAdapter.Notes, var key]
            ? "https://bsky.app/profile/" + did + "/post/" + key
            : null;
}
