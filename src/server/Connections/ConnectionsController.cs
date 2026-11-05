using System.ComponentModel;
using Koan.Data.Abstractions;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Sorting;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Connections;

/// <summary>A participant's Connections page, one resource both ways (<see cref="ConnectionsPage"/>): their own, so a
/// visitor signs in and comes back here. It reads their Connection, at most <see cref="Shown"/> of its receipts, newest
/// first, and the articles those name, each by key. <see cref="OfferAgainPath"/> brings back the offer they said not now
/// to.</summary>
public sealed class ConnectionsController(KoanWebBaseUrl baseUrl, Nudge nudge) : ControllerBase
{
    public const string Path = "/settings/connections";

    public const string OfferAgainPath = Path + "/offer-again";

    /// <summary>The most articles the page lists.</summary>
    public const int Shown = 50;

    [HttpGet(Path)]
    [Tool(Name = "read_connections")]
    [Description("Read your Connections: whether your public articles publish under your own atproto account, and where each one stands there — publishing, published, updated, or what could not be done and when it is tried again.")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var address = baseUrl.Resolve(HttpContext);
        if (PostWrites.PersonId(User) is not { } personId)
            return Redirect(address + SignInController.Path + "?return=" + Uri.EscapeDataString(baseUrl.PathBase + Path));
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);

        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        var participant = await Participant.Get(personId, ct);
        var handle = participant?.Handle is { Length: > 0 } known ? known : personId;
        var connection = await Connection.Get(personId, ct);
        var receipts = connection is null ? [] : await Receipt.Query(receipt => receipt.ConnectionId == personId,
            new QueryDefinition { Page = 1, PageSize = Shown }.WithSort<Receipt>(sort => sort.OrderByDescending(receipt => receipt.At)), ct);
        var posts = receipts.Count == 0 ? new Dictionary<string, Post>()
            : (await Post.Get(receipts.Select(receipt => receipt.PostId).ToList(), ct)).OfType<Post>().ToDictionary(post => post.Id, StringComparer.Ordinal);

        var articles = receipts.Where(receipt => posts.ContainsKey(receipt.PostId)).Select(receipt =>
        {
            var post = posts[receipt.PostId];
            var state = Receipt.StateOf(receipt, post, connection!);
            return new ConnectionsPage.Article(post.Title ?? "", address + AtprotoAdapter.Path(post), Word(state), Sentence(state, connection!),
                receipt.IsLive() && receipt.Note is { } note ? Publishing.Bluesky(note) : null, receipt.At);
        }).ToList();

        return Ok(new ConnectionsPage(space.Name, address + "/", await PostWrites.Reader(personId, place, address, baseUrl.PathBase + Path, ct),
            Account(connection, handle, address), articles));
    }

    /// <summary>Brings back the offer to publish under one's own account, after saying not now to it.</summary>
    [HttpPost(OfferAgainPath)]
    public async Task<IActionResult> OfferAgain(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (PostWrites.PersonId(User) is not { } personId) return Answers.Refused(HttpContext, Capabilities.SignIn);
        var outcome = await new OfferAgain(personId).Send(ct);
        if (outcome is not Outcome<Connection?>.Done) return Answers.NotDone(HttpContext, outcome);
        nudge.Changed();
        Response.Headers.Location = baseUrl.Resolve(HttpContext) + Path;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    private static ConnectionsPage.Account Account(Connection? connection, string handle, string address)
    {
        if (connection is null)
            return new("@" + handle, "none", "Your public articles can go out on Bluesky under your own account. Each one offers it, and Bluesky is asked for permission the first time you choose it.",
                null, null, null, null);
        if (connection.State == ConnectionState.NotNow)
            return new("@" + handle, "not-now", "You said not now to publishing on Bluesky, so your articles don't offer it.",
                null, null, null, Form.Posting(address + OfferAgainPath));
        if (connection.NeedsGrant)
            return new("@" + handle, "grant", Syndication.Ungranted + " Grant it again to go on publishing.",
                connection.Since, null, Form.Posting(address + PublishController.GrantPath), null);
        if (connection.Trouble is { } trouble)
            return new("@" + handle, "trouble", trouble, connection.Since, connection.RetryAt, null, null);
        return new("@" + handle, "publishing", "Your public articles go out on Bluesky as @" + handle + ", and follow your edits and removals.",
            connection.Since, null, null, null);
    }

    public static string Word(ArticleState state) => state switch
    {
        ArticleState.NeedsGrant => "grant",
        _ => state.ToString().ToLowerInvariant()
    };

    private static string Sentence(ArticleState state, Connection connection) => state switch
    {
        ArticleState.Publishing => "Publishing…",
        ArticleState.Published => "Published",
        ArticleState.Updating => "Updating…",
        ArticleState.Updated => "Updated",
        ArticleState.Trying => connection.Trouble ?? Syndication.Unreachable,
        ArticleState.NeedsGrant => "Waiting for permission",
        _ => "Retracted"
    };
}
