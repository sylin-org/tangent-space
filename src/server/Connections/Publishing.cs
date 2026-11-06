using Tangent.Posts;
using Tangent.Render;

namespace Tangent.Connections;

/// <summary>Where an article stands on its author's own account, as its page tells its author: the state's word — the
/// word their Connections page gives it (<see cref="ConnectionsController.Word"/>), or <see cref="Offer"/> before it was
/// ever out — its sentence, the form that publishes it, or asks for the grant again, where they may, the form that says
/// not now to the first offer, the article's Bluesky post once it is out, and their Connections page.
/// <see cref="Document"/> is the article's document record, which the page's head names for everyone once it is out.</summary>
public sealed record Publishing(string State, string Sentence, string? Detail, Form? Publish, Form? NotNow, string? Elsewhere, string Connections) : IPart
{
    public const string Offer = "offer";

    /// <summary>The article's line for <paramref name="reader"/> and its document, from two keyed reads: the receipt of
    /// its author's Connection and, for its author, that Connection. A reader other than the author and an author who said
    /// not now read no line; an article that is not public or is removed reads none either, unless its copy is still out,
    /// which its author is told, with what takes it down; and one written as an account they are no longer bound to stays
    /// with that account (<see cref="EarlierSentence"/>).</summary>
    public static async Task<(Publishing? Line, string? Document)> For(Post root, Participants.Participant? reader, bool open,
        string articleAddress, string baseAddress, CancellationToken ct)
    {
        var readerId = reader?.Id;
        var handle = reader?.ShownHandle();
        if (root.Kind != PostKind.Article || root.ParentId is not null) return (null, null);
        var receipt = await Receipt.Get(Receipt.Key(root.AuthorId, root.Id), ct);
        var shown = open && !root.Removed;
        var document = receipt?.IsLive() == true && shown ? receipt.Document : null;
        if (readerId != root.AuthorId || (!shown && receipt?.IsLive() != true)) return (null, document);

        var connection = await Connection.Get(readerId, ct);
        var connections = baseAddress + ConnectionsController.Path;
        var elsewhere = receipt?.Note is { } note ? Bluesky(note) : null;
        if (!shown && receipt!.Repository() != reader!.Did)
            return (new Publishing(ConnectionsController.Word(ArticleState.Earlier), receipt.Stays ? StaysSentence : LeavingSentence, null, null, null,
                elsewhere, connections), document);
        if (!shown)
            return (connection is null ? null : new Publishing(ConnectionsController.Word(ArticleState.Remains), RemainsSentence, Undoing(connection),
                connection.NeedsGrant ? Form.Posting(baseAddress + PublishController.GrantPath) : null, null, elsewhere, connections), document);

        if (root.AuthorDid != reader!.Did)
            return (new Publishing(ConnectionsController.Word(ArticleState.Earlier), EarlierSentence, null, null, null, elsewhere, connections), document);

        var publish = Form.Posting(articleAddress + "/" + PublishController.Segment);
        if (connection is null)
            return (new Publishing(Offer, "Publish this on Bluesky as you?",
                "It goes out under your own account" + (handle is null ? "" : ", @" + handle) + ", and follows your edits and removal.",
                publish, Form.Posting(baseAddress + PublishController.NotNowPath, new Form.Field(PublishController.BackField, "The page to come back to.",
                    PublishController.Back(articleAddress, baseAddress))), null, connections), document);
        if (connection.State == ConnectionState.NotNow) return (null, document);
        if (receipt is null || receipt.Retracted)
            return (connection.NeedsGrant
                ? new Publishing(ConnectionsController.Word(ArticleState.NeedsGrant), Syndication.Ungranted, null, publish, null, null, connections)
                : new Publishing(Offer, "Publish this on Bluesky too?", null, publish, null, null, connections), document);

        var state = Receipt.StateOf(receipt, root, connection, open: true, reader.Did);
        var word = ConnectionsController.Word(state);
        return (state switch
        {
            ArticleState.EditedByOthers => new Publishing(word, "Someone else edited this since it went out. Publish this version as yours?",
                "Until you do, your copy on Bluesky stays as you wrote it.", publish, null, elsewhere, connections),
            ArticleState.Publishing => new Publishing(word, "Publishing on Bluesky as you…", null, null, null, null, connections),
            ArticleState.Updating => new Publishing(word, "Updating it on Bluesky…", null, null, null, elsewhere, connections),
            ArticleState.Trying => new Publishing(word, connection.Trouble ?? Syndication.Unreachable, null, null, null, elsewhere, connections),
            ArticleState.NeedsGrant => new Publishing(word, Syndication.Ungranted, null, publish, null, elsewhere, connections),
            ArticleState.Updated => new Publishing(word, "Updated on Bluesky.", null, null, null, elsewhere, connections),
            _ => new Publishing(word, "Published on Bluesky as you.", null, null, null, elsewhere, connections)
        }, document);
    }

    /// <summary>What the author of an article written as a Bluesky account they are no longer bound to reads: it, and any
    /// copy of it, stays with that account, and their Connection publishes only what they write as the one they hold.</summary>
    public const string EarlierSentence = "Written as your earlier Bluesky account, so it stays with that account.";

    /// <summary>What the author reads of a copy out under an earlier account of theirs that should no longer be out, while
    /// their Connection takes it down through that account's grant.</summary>
    public const string LeavingSentence = "This is still public on Bluesky under your earlier account, though it no longer is here. It is being taken down.";

    /// <summary>What the author reads of such a copy once that account's grant no longer lets the place take it down.</summary>
    public const string StaysSentence = "This stays public on Bluesky under your earlier account, which no longer lets this place take it down.";

    /// <summary>What the author of an article that is no longer public here, or is removed, reads while its copy is still
    /// out.</summary>
    public const string RemainsSentence = "This is still public on Bluesky, though it no longer is here.";

    /// <summary>What takes a copy that remains down: the grant again, where the Connection needs it; otherwise the next
    /// attempt, after the trouble it says, or the next run.</summary>
    public static string Undoing(Connection connection)
        => connection.NeedsGrant ? "Grant it again to take it down."
            : connection.Trouble is { } trouble ? trouble
            : "It is being taken down.";

    /// <summary>Whether the line's form publishes the article, rather than asking for the grant again.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool Publishes => State == Offer || State == ConnectionsController.Word(ArticleState.EditedByOthers);

    /// <summary>What its author may do from the line: publish, or grant again; say not now; view the Bluesky post; open their
    /// Connections.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public IReadOnlyList<IPart> Answers =>
    [
        .. Publish is { } publish ? new IPart[] { publish with { Tool = "publish", Submit = Publishes ? "Publish" : "Grant it again" } } : [],
        .. NotNow is { } notNow ? new IPart[] { notNow.AsGiven() with { Tool = "not-now", Submit = "Not now", Quiet = true } } : [],
        .. Elsewhere is { } elsewhere ? new IPart[] { new LinkButton("View on Bluesky", elsewhere) { Outside = true } } : [],
        .. State != Offer ? new IPart[] { new LinkButton("Connections", Connections) } : []
    ];

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
