using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Model;
using Koan.Data.Core.Sorting;
using Tangent.Posts;

namespace Tangent.Connections;

/// <summary>What one Connection delivered of one article, keyed by the two: the version of the Post it carries, the
/// <c>site.standard.document</c> record in its author's repository and the Bluesky post that embeds it, each by its
/// <c>at://</c> address, or that it was retracted, and whether its author took it down while the article stays here
/// (<see cref="Withheld"/>). <see cref="Version"/> is zero while the article waits to be published.
/// The state its participant reads is this against the Post's own version (<see cref="StateOf"/>); a copy stays in the
/// repository its address names (<see cref="Repository"/>), whatever account its participant holds since.</summary>
public sealed class Receipt : Entity<Receipt>
{
    public static string Key(string connectionId, string postId) => connectionId + "/" + postId;

    /// <summary>In <c>ix_receipt_connections</c> before when it last changed, so a Connection's receipts, newest first,
    /// are one range.</summary>
    [Index(Name = "ix_receipt_connections", Order = 0)]
    public string ConnectionId { get; set; } = "";

    public string PostId { get; set; } = "";

    /// <summary>In <c>ix_receipt_tangents</c> before whether it was retracted, so the copies still out of one Tangent's
    /// articles are one range (<see cref="Out"/>).</summary>
    [Index(Name = "ix_receipt_tangents", Order = 0)]
    public string TangentId { get; set; } = "";

    /// <summary>The Post's version this delivered; zero while it waits to be published.</summary>
    public long Version { get; set; }

    /// <summary>The Post's version its author chose to publish as theirs: words someone else edited go out only once they
    /// choose them.</summary>
    public long Chosen { get; set; }

    /// <summary>The document record's address and content id, once published.</summary>
    public string? Document { get; set; }

    public string? DocumentCid { get; set; }

    /// <summary>The Bluesky post that embeds the article, and its content id, once published.</summary>
    public string? Note { get; set; }

    public string? NoteCid { get; set; }

    /// <summary>Whether a later version replaced the one first published.</summary>
    public bool Updated { get; set; }

    [Index(Name = "ix_receipt_tangents", Order = 1)]
    public bool Retracted { get; set; }

    /// <summary>Whether its copy, out under an account its participant is no longer bound to, stays there: that account's
    /// grant no longer lets the place take it down.</summary>
    public bool Stays { get; set; }

    /// <summary>Whether its author took the article off Bluesky while it stays here: its copy comes down, and nothing of it
    /// goes out again until they publish it again.</summary>
    public bool Withheld { get; set; }

    [Index(Name = "ix_receipt_connections", Order = 1)]
    public DateTimeOffset At { get; set; }

    /// <summary>The DID of the repository the copies this receipt names are in, from their <c>at://</c> address; null
    /// before anything went out.</summary>
    public string? Repository() => (Document ?? Note)?.Split('/') is ["at:", "", var did, ..] ? did : null;

    /// <summary>Whether the network holds a copy this receipt names.</summary>
    public bool IsLive() => Document is not null && !Retracted;

    /// <summary>Whether <paramref name="post"/> is ahead of what this delivered: a later version, or moved to another Tangent
    /// since, whose address the copy does not name yet.</summary>
    public bool Behind(Post post) => Version < post.Version || TangentId != post.TangentId;

    /// <summary>How many copies of the articles of the Tangent <paramref name="tangentId"/> are out, at most
    /// <paramref name="limit"/>, and whether more are: one read of <c>ix_receipt_tangents</c>.</summary>
    public static async Task<(int Count, bool AtLeast)> Out(string tangentId, int limit, CancellationToken ct)
    {
        var live = await Query(receipt => receipt.TangentId == tangentId && !receipt.Retracted && receipt.Document != null,
            new QueryDefinition { Page = 1, PageSize = limit + 1 }, ct);
        return (Math.Min(live.Count, limit), live.Count > limit);
    }

    /// <summary>The most receipts a page of a Connection's holds.</summary>
    public const int PageSize = 50;

    /// <summary>A page of a Connection's receipts, newest first, and where the next one starts; <see cref="Next"/> is null on
    /// the last.</summary>
    public sealed record Listing(IReadOnlyList<Receipt> Receipts, Cursor? Next);

    /// <summary>The place of the last receipt a page held: when it last changed and its article's TID, which orders receipts
    /// of the same instant.</summary>
    public sealed record Cursor(DateTimeOffset At, string PostId)
    {
        /// <summary>The cursor a query carries, or null when the text is not one.</summary>
        public static Cursor? Parse(string? text)
        {
            var parts = text?.Split('.') ?? [];
            if (parts.Length != 2 || !Tid.IsValid(parts[1])) return null;
            if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks) return null;
            return new Cursor(new DateTimeOffset(ticks, TimeSpan.Zero), parts[1]);
        }

        public override string ToString() => At.UtcTicks.ToString(CultureInfo.InvariantCulture) + "." + PostId;
    }

    /// <summary>The page of the Connection <paramref name="connectionId"/>'s receipts after <paramref name="after"/>, or the
    /// first: one read of <c>ix_receipt_connections</c>, limited to the page and one more, which says whether a next page
    /// exists. Past the cursor means changed no later than it, and at its own instant, an article before its.</summary>
    public static async Task<Listing> Of(string connectionId, Cursor? after, CancellationToken ct)
    {
        var at = FieldPath.Of(nameof(At));
        var post = FieldPath.Of(nameof(PostId));
        var seek = after is null ? null : Filter.All(
            Filter.On(at, FilterOperator.Lte, FilterValue.Of(after.At)),
            Filter.Any(Filter.On(at, FilterOperator.Lt, FilterValue.Of(after.At)), Filter.On(post, FilterOperator.Lt, FilterValue.Of(after.PostId))));
        var read = await Query(receipt => receipt.ConnectionId == connectionId,
            new QueryDefinition { Filter = seek, Page = 1, PageSize = PageSize + 1 }
                .WithSort<Receipt>(sort => sort.OrderByDescending(receipt => receipt.At).ThenByDescending(receipt => receipt.PostId)), ct);
        return read.Count > PageSize
            ? new Listing([.. read.Take(PageSize)], new Cursor(read[PageSize - 1].At, read[PageSize - 1].PostId))
            : new Listing(read, null);
    }

    /// <summary>Whether the copy may carry <paramref name="post"/> as it stands: its words are its author's own, or they
    /// chose this version as theirs.</summary>
    public static bool Theirs(Post post, Receipt? receipt) => post.EditedBy is null || receipt?.Chosen >= post.Version;

    /// <summary>The state of an article against what its Connection delivered, as its author reads it; <paramref name="open"/>
    /// says whether it should be out, public and standing (<see cref="Publishing.Open"/>), so a copy still out of one that
    /// should not be remains, and says so, until its retraction lands, and one that never went out is held; one its author
    /// took down is retracting until its copy is down, then retracted; one that moved reads updating until its copy names where
    /// it stands. An article deleted (<paramref name="post"/> null) is read from the receipt alone: written as an earlier account where its
    /// copy is in another repository than <paramref name="did"/>'s.</summary>
    public static ArticleState StateOf(Receipt receipt, Post? post, Connection connection, bool open, string did)
    {
        if ((post?.AuthorDid ?? receipt.Repository()) != did) return ArticleState.Earlier;
        if (!open) return receipt.IsLive() ? ArticleState.Remains : receipt.Document is null ? ArticleState.Held : ArticleState.Retracted;
        if (receipt.Withheld) return receipt.IsLive() ? ArticleState.Retracting : ArticleState.Retracted;
        if (receipt.Retracted && receipt.Document is not null) return ArticleState.Retracted;
        var behind = post is not null && receipt.Behind(post);
        if (behind && !Theirs(post!, receipt)) return ArticleState.EditedByOthers;
        if (behind && connection.NeedsGrant) return ArticleState.NeedsGrant;
        if (behind && connection.Trouble is not null) return ArticleState.Trying;
        if (receipt.Version == 0) return ArticleState.Publishing;
        if (behind) return ArticleState.Updating;
        return receipt.Updated ? ArticleState.Updated : ArticleState.Published;
    }
}

/// <summary>Where an article stands on its author's account.</summary>
public enum ArticleState
{
    Publishing,
    Published,
    Updating,
    Updated,
    Trying,
    NeedsGrant,

    /// <summary>Someone else edited it since: the copy stays at its author's words until they choose the new version.</summary>
    EditedByOthers,

    /// <summary>It is no longer public here, or is removed, and its copy is still out: taken down at the next run, or once
    /// the grant comes back.</summary>
    Remains,

    /// <summary>It never went out, and does not while it is not public here or is removed.</summary>
    Held,

    /// <summary>Its author took it off Bluesky while it stays here: its copy comes down at the next run, or once the grant
    /// comes back.</summary>
    Retracting,
    Retracted,

    /// <summary>Its author wrote it as a Bluesky account they are no longer bound to: it, and any copy of it, stays with
    /// that account.</summary>
    Earlier
}
