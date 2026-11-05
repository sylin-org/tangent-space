using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Tangent.Posts;

namespace Tangent.Connections;

/// <summary>What one Connection delivered of one article, keyed by the two: the version of the Post it carries, the
/// <c>site.standard.document</c> record in its author's repository and the Bluesky post that embeds it, each by its
/// <c>at://</c> address, or that it was retracted. <see cref="Version"/> is zero while the article waits to be published.
/// The state its participant reads is this against the Post's own version (<see cref="StateOf"/>).</summary>
public sealed class Receipt : Entity<Receipt>
{
    public static string Key(string connectionId, string postId) => connectionId + "/" + postId;

    /// <summary>In <c>ix_receipt_connections</c> before when it last changed, so a Connection's receipts, newest first,
    /// are one range.</summary>
    [Index(Name = "ix_receipt_connections", Order = 0)]
    public string ConnectionId { get; set; } = "";

    public string PostId { get; set; } = "";

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

    public bool Retracted { get; set; }

    [Index(Name = "ix_receipt_connections", Order = 1)]
    public DateTimeOffset At { get; set; }

    /// <summary>Whether the network holds a copy this receipt names.</summary>
    public bool IsLive() => Document is not null && !Retracted;

    /// <summary>Whether the copy may carry <paramref name="post"/> as it stands: its words are its author's own, or they
    /// chose this version as theirs.</summary>
    public static bool Theirs(Post post, Receipt? receipt) => post.EditedBy is null || receipt?.Chosen >= post.Version;

    /// <summary>The state of an article against what its Connection delivered, as its author reads it.</summary>
    public static ArticleState StateOf(Receipt receipt, Post? post, Connection connection)
    {
        if (receipt.Retracted) return ArticleState.Retracted;
        var behind = post is not null && receipt.Version < post.Version;
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
    Retracted
}
