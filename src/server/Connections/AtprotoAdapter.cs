using System.Globalization;
using System.Text.Json.Nodes;
using Koan.Media;
using Koan.Media.Core.Pipeline;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Koan.Web.Hosting;
using Tangent.Posts;
using Tangent.Space;

namespace Tangent.Connections;

/// <summary>What the atproto package wrote for one article: its document and the Bluesky post that embeds it.</summary>
public sealed record Written(AtprotoRecord Document, AtprotoRecord Note);

/// <summary>The atproto package: an article as a <c>site.standard.document</c> record in its author's own repository,
/// keyed by the Post's TID, its cover as a blob, its page's address as canonical, and the place as its <c>site</c> — the
/// proprietor's <c>site.standard.publication</c> where they published it, the place's address otherwise; a Bluesky post
/// under the same key that embeds the article's page, written once; and the place itself as the publication, under the
/// proprietor's account. Every write is a put at a fixed key, so one sent again lands once, and a retraction deletes
/// both records of the article.</summary>
public sealed class AtprotoAdapter(IAtprotoRepository repository, KoanWebBaseUrl baseUrl)
{
    public const string Documents = "site.standard.document";
    public const string Publications = "site.standard.publication";
    public const string Notes = "app.bsky.feed.post";

    /// <summary>The grant an author gives: their documents, their Bluesky posts and the images both carry.</summary>
    public static readonly IReadOnlyList<string> Scopes = ["atproto", "repo:" + Documents, "repo:" + Notes, "blob:image/*"];

    /// <summary>The proprietor's grant, which also writes the place as their publication.</summary>
    public static readonly IReadOnlyList<string> ProprietorScopes = [.. Scopes, "repo:" + Publications];

    /// <summary>The largest cover sent as a blob, within what a document and an embed accept.</summary>
    public const int CoverBytes = 950 * 1024;

    /// <summary>The longest description a Bluesky embed carries.</summary>
    private const int DescriptionLength = 300;

    public static IReadOnlyList<string> ScopesFor(string personId, TangentSpace space)
        => space.ProprietorId == personId ? ProprietorScopes : Scopes;

    /// <summary>Whether the account <paramref name="did"/> the participant is bound to lets the place write what their
    /// Connection writes.</summary>
    public Task<bool> MayWrite(string personId, string did, TangentSpace space, CancellationToken ct)
        => repository.Holds(did, ScopesFor(personId, space), ct);

    /// <summary>Whether the account <paramref name="did"/>, one its participant was bound to before, still lets the place
    /// change what <paramref name="scopes"/> cover there, through the grant Koan keeps for that DID.</summary>
    public Task<bool> MayChange(string did, IReadOnlyList<string> scopes, CancellationToken ct) => repository.Holds(did, scopes, ct);

    /// <summary>The address of an article's page, canonical wherever it is published.</summary>
    public string Canonical(Post root) => Base() + Path(root);

    public static string Path(Post root) => "/t/" + Uri.EscapeDataString(root.TangentId) + "/" + Uri.EscapeDataString(root.Slug ?? root.Id);

    /// <summary>Publishes or updates <paramref name="article"/> under the account it was written as. The Bluesky post is
    /// written the first time and kept after; the document is put every time, naming the post.</summary>
    public async Task<Written> Deliver(Post article, Receipt? receipt, Publication? publication, CancellationToken ct)
    {
        var did = article.AuthorDid;
        var cover = article.CoverId is { } id ? await Cover(did, id, ct) : null;
        var text = Mentions.PlainText(article, await QuotedPost.People(article, ct));
        var description = Lead(text);
        var title = article.Title ?? "";
        var canonical = Canonical(article);

        var note = receipt is { Note: { } noteUri, NoteCid: { } noteCid, Retracted: false }
            ? new AtprotoRecord(noteUri, noteCid)
            : await repository.Put(did, Notes, article.Id, Note(title, description, canonical, cover), ct);

        var document = new JsonObject
        {
            ["$type"] = Documents,
            ["site"] = publication?.Uri ?? Base(),
            ["path"] = Path(article),
            ["title"] = title,
            ["publishedAt"] = Stamp(article.CreatedAt),
            ["bskyPostRef"] = new JsonObject { ["uri"] = note.Uri, ["cid"] = note.Cid }
        };
        if (description.Length > 0) document["description"] = description;
        if (text.Length > 0) document["textContent"] = text;
        if (cover is not null) document["coverImage"] = cover.DeepClone();
        if (article.ChangedAt is { } changed) document["updatedAt"] = Stamp(changed);
        return new Written(await repository.Put(did, Documents, article.Id, document, ct), note);
    }

    /// <summary>Deletes both copies a receipt names from the repository they are in; ones already gone are deleted.</summary>
    public async Task Retract(Receipt receipt, CancellationToken ct)
    {
        var did = receipt.Repository() ?? throw new InvalidOperationException("A receipt with nothing out has nothing to retract.");
        await repository.Delete(did, Documents, receipt.PostId, ct);
        await repository.Delete(did, Notes, receipt.PostId, ct);
    }

    /// <summary>Puts the place as the proprietor's publication under the account <paramref name="did"/> they are bound to,
    /// at the key it already has or a new one.</summary>
    public Task<AtprotoRecord> Publish(TangentSpace space, string did, string key, CancellationToken ct)
    {
        var record = new JsonObject { ["$type"] = Publications, ["url"] = Base(), ["name"] = space.Name };
        if (space.Description.Length > 0) record["description"] = space.Description;
        return repository.Put(did, Publications, key, record, ct);
    }

    /// <summary>Deletes the place's publication record from the account it is under.</summary>
    public Task Withdraw(Publication publication, CancellationToken ct)
        => repository.Delete(publication.Did, Publications, publication.Key, ct);

    /// <summary>What the place's publication record says, to tell when it has changed.</summary>
    public string Fingerprint(TangentSpace space) => Base() + "\n" + space.Name + "\n" + space.Description;

    private static JsonObject Note(string title, string description, string canonical, JsonObject? cover)
    {
        var external = new JsonObject { ["uri"] = canonical, ["title"] = title, ["description"] = description };
        if (cover is not null) external["thumb"] = cover.DeepClone();
        return new JsonObject
        {
            ["$type"] = Notes,
            ["text"] = title,
            ["createdAt"] = Stamp(DateTimeOffset.UtcNow),
            ["embed"] = new JsonObject { ["$type"] = "app.bsky.embed.external", ["external"] = external }
        };
    }

    /// <summary>The cover as a blob of at most <see cref="CoverBytes"/>, or none where it cannot be read or made that small.</summary>
    private async Task<JsonObject?> Cover(string did, string mediaId, CancellationToken ct)
    {
        if (await Media.Get(mediaId, ct) is not { } media) return null;
        await using var kept = await media.OpenRead(ct);
        using var sent = new MemoryStream();
        await kept.AsMedia().ResizeFit(1600, 1600).Strip().FlattenTo("jpeg", 82).WriteToAsync(sent, ct);
        return sent.Length > CoverBytes ? null : await repository.Upload(did, sent.ToArray(), "image/jpeg", ct);
    }

    private static string Lead(string text)
    {
        var line = string.Join(' ', text.Split((char[])['\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= DescriptionLength ? line : line[..(DescriptionLength - 1)].TrimEnd() + "…";
    }

    private static string Stamp(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>The place's address, which every canonical address is under; null until it has one.</summary>
    public string? Address => baseUrl.Current?.TrimEnd('/');

    private string Base() => Address ?? throw new InvalidOperationException("The place has no address to publish under.");
}
