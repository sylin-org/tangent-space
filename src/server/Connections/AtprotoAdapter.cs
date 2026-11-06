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
/// proprietor's account, with its mark as its icon and its first four tokens as its basic theme. Every write is a put at
/// a fixed key, so one sent again lands once, and a retraction deletes both records of the article.</summary>
public sealed class AtprotoAdapter(IAtprotoRepository repository, KoanWebBaseUrl baseUrl, IWebHostEnvironment host)
{
    public const string Documents = "site.standard.document";
    public const string Publications = "site.standard.publication";
    public const string Notes = "app.bsky.feed.post";
    public const string BasicTheme = "site.standard.theme.basic";
    public const string Rgb = "site.standard.theme.color#rgb";

    /// <summary>The grant an author gives: their documents, their Bluesky posts and the images both carry.</summary>
    public static readonly IReadOnlyList<string> Scopes = ["atproto", "repo:" + Documents, "repo:" + Notes, "blob:image/*"];

    /// <summary>The proprietor's grant, which also writes the place as their publication.</summary>
    public static readonly IReadOnlyList<string> ProprietorScopes = [.. Scopes, "repo:" + Publications];

    /// <summary>The largest cover sent as a blob, within what a document and an embed accept.</summary>
    public const int CoverBytes = 950 * 1024;

    /// <summary>The side of the publication's icon in pixels: square, as the lexicon asks, at twice the least it asks for.</summary>
    public const int IconEdge = 512;

    /// <summary>The largest icon sent as a blob, the lexicon's own bound.</summary>
    public const int IconBytes = 1_000_000;

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
    /// at the key it already has or a new one: its address, its name, and, each where it has one, its description,
    /// <paramref name="icon"/>, a blob in that account's repository, and its first four tokens as its basic theme.</summary>
    public Task<AtprotoRecord> Publish(TangentSpace space, string did, string key, JsonObject? icon, CancellationToken ct)
    {
        var record = new JsonObject { ["$type"] = Publications, ["url"] = Base(), ["name"] = space.Name };
        if (space.Description.Length > 0) record["description"] = space.Description;
        if (icon is not null) record["icon"] = icon.DeepClone();
        if (Colours(space) is { } colours)
            record["basicTheme"] = new JsonObject
            {
                ["$type"] = BasicTheme,
                ["background"] = Colour(colours.Background),
                ["foreground"] = Colour(colours.Foreground),
                ["accent"] = Colour(colours.Accent),
                ["accentForeground"] = Colour(colours.AccentForeground)
            };
        return repository.Put(did, Publications, key, record, ct);
    }

    /// <summary>The place's <paramref name="mark"/>, where it is a picture, as the publication's icon in the repository of
    /// <paramref name="did"/>: cut square from its middle, as every page draws it, at <see cref="IconEdge"/>, one frame of
    /// PNG, which keeps a logo's transparency, as a blob of at most <see cref="IconBytes"/>; none for its letter, or where
    /// the picture cannot be read or made that small.</summary>
    public async Task<JsonObject?> Icon(string mark, string did, CancellationToken ct)
    {
        if (Media.IdIn(mark) is not { } id || await Media.Get(id, ct) is not { } media) return null;
        await using var kept = await media.OpenRead(ct);
        using var sent = new MemoryStream();
        await kept.AsMedia().ResizeCover(IconEdge, IconEdge).Strip().FlattenTo("png").WriteToAsync(sent, ct);
        return sent.Length > IconBytes ? null : await repository.Upload(did, sent.ToArray(), "image/png", ct);
    }

    /// <summary>Deletes the place's publication record from the account it is under.</summary>
    public Task Withdraw(Publication publication, CancellationToken ct)
        => repository.Delete(publication.Did, Publications, publication.Key, ct);

    /// <summary>What the place's publication record says, to tell when it has changed: its address, name and description,
    /// the mark its icon is made from and its first four tokens.</summary>
    public string Fingerprint(TangentSpace space)
        => string.Join("\n", Base(), space.Name, space.Description, space.Appearance.Mark, Colours(space)?.ToString() ?? "");

    /// <summary>The place's first four tokens, from the stylesheets it serves; none under a base's own palette.</summary>
    private FirstFour? Colours(TangentSpace space) => FirstFour.Of(space.Appearance, host.WebRootFileProvider);

    /// <summary>A colour as <c>#rrggbb</c>, as <c>site.standard.theme.color#rgb</c> holds it.</summary>
    private static JsonObject Colour(string hex)
    {
        var rgb = Convert.FromHexString(hex.AsSpan(1));
        return new JsonObject { ["$type"] = Rgb, ["r"] = rgb[0], ["g"] = rgb[1], ["b"] = rgb[2] };
    }

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
