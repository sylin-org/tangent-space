using System.ComponentModel;
using Koan.Data.Abstractions;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Sorting;
using Koan.Web.Attributes;
using Koan.Web.Auth.Infrastructure;
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
/// visitor's browser signs in and comes back here, and a visitor's JSON or tool call is refused with <c>sign-in</c>. It
/// reads their Connection, at most <see cref="Shown"/> of its receipts, newest first, the articles those name, each by
/// key, and whether each of their Tangents is public. A permission Bluesky did not give comes back here with Koan's code
/// in <c>error</c>, and the article it was for in <see cref="ArticleQuery"/>, which the page says in words above the rest.
/// <see cref="OfferAgainPath"/> brings back the offer they said not now to.</summary>
public sealed class ConnectionsController(KoanWebBaseUrl baseUrl, Nudge nudge) : ControllerBase
{
    public const string Path = "/settings/connections";

    public const string OfferAgainPath = Path + "/offer-again";

    /// <summary>The most articles the page lists.</summary>
    public const int Shown = 50;

    /// <summary>The query a permission that was not given names its article in, by Id.</summary>
    public const string ArticleQuery = "article";

    [HttpGet(Path)]
    [Tool(Name = "read_connections")]
    [Description("Read your Connections: whether your public articles publish under your own atproto account, and where each one stands there — publishing, published, updated, edited by someone else, still out though no longer public here, held back while it is not public here, or what could not be done and when it is tried again.")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var address = baseUrl.Resolve(HttpContext);
        if (PostWrites.PersonId(User) is not { } personId)
            return PostWrites.WantsJson(Request)
                ? Answers.Refused(HttpContext, Capabilities.SignIn)
                : Redirect(address + SignInController.Path + "?return=" + Uri.EscapeDataString(baseUrl.PathBase + Path));
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);

        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        var participant = await Participant.Get(personId, ct);
        var handle = participant?.ShownHandle() ?? personId;
        var connection = await Connection.Get(personId, ct);
        var receipts = connection is null ? [] : await Receipt.Query(receipt => receipt.ConnectionId == personId,
            new QueryDefinition { Page = 1, PageSize = Shown }.WithSort<Receipt>(sort => sort.OrderByDescending(receipt => receipt.At)), ct);
        var posts = receipts.Count == 0 ? new Dictionary<string, Post>()
            : (await Post.Get(receipts.Select(receipt => receipt.PostId).ToList(), ct)).OfType<Post>().ToDictionary(post => post.Id, StringComparer.Ordinal);

        var open = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var tangentId in posts.Values.Select(post => post.TangentId).Distinct(StringComparer.Ordinal))
            open[tangentId] = await Publishing.Open(space, tangentId, ct);

        var articles = receipts.Where(receipt => posts.ContainsKey(receipt.PostId)).Select(receipt =>
        {
            var post = posts[receipt.PostId];
            var shown = open[post.TangentId] && !post.Removed;
            var state = Receipt.StateOf(receipt, post, connection!, shown, participant?.Did ?? "");
            var sentence = state != ArticleState.Earlier ? Sentence(state, connection!)
                : !shown && receipt.IsLive() ? (receipt.Stays ? Publishing.StaysSentence : Publishing.LeavingSentence)
                : Sentence(state, connection!);
            return new ConnectionsPage.Article(post.Title ?? "", address + AtprotoAdapter.Path(post), Word(state), sentence,
                receipt.IsLive() && receipt.Note is { } note ? Publishing.Bluesky(note) : null, receipt.At);
        }).ToList();

        var another = Request.Query.ContainsKey(AnotherAccount.OpenQuery);
        return Ok(new ConnectionsPage(space.Name, address + "/", await PostWrites.Reader(personId, place, address, baseUrl.PathBase + Path, ct),
            Account(connection, handle, address, articles.Any(article => article.State == Word(ArticleState.Remains))), articles,
            Another(participant, handle, address, another))
        {
            Failure = another ? null : await Failure(personId, address, ct)
        });
    }

    /// <summary>Use another Bluesky account, as this page draws it: coming back here bound to the new account; open, with
    /// the handle typed kept, where a refused binding or the sign-in that answers one came back, saying why it was refused,
    /// and, where Koan wants a fresh sign-in first, the sign-in as the account held now, which comes back here.</summary>
    private AnotherAccount.Offer Another(Participant? participant, string handle, string address, bool open)
    {
        var typed = AnotherAccount.Kept(Request.Query[AnotherAccount.HandleQuery].ToString());
        var code = open ? Request.Query[SignInFailureReturn.CodeParameter].ToString() : null;
        var landing = AnotherAccount.FailurePath + (typed is null ? "" : "&" + AnotherAccount.HandleQuery + "=" + Uri.EscapeDataString(typed));
        var again = code == SignInFailureReturn.Code(SignInFailure.SignInFirst) && participant is { Did.Length: > 0 }
            ? new Form(address + SignInController.ChallengePath, "get",
            [
                new Form.Field("identifier", "The account you hold now.", participant.Did),
                new Form.Field(AuthConstants.Query.Return, "This page, with the form open.", baseUrl.PathBase + landing),
                new Form.Field(AuthConstants.Query.Failure, "This page, saying why.", landing)
            ])
            : null;
        return new AnotherAccount.Offer(address + AnotherAccount.Route, baseUrl.PathBase + Path, AnotherAccount.FailurePath, handle, typed,
            AnotherAccount.Why(code), again, open);
    }

    /// <summary>What a permission Bluesky did not give says, from Koan's code in <c>error</c>, naming the reader's own
    /// article where <see cref="ArticleQuery"/> names one, with the way back to it; null where no code came.</summary>
    private async Task<ConnectionsPage.Failed?> Failure(string personId, string address, CancellationToken ct)
    {
        if (Request.Query[SignInFailureReturn.CodeParameter].ToString() is not { Length: > 0 } code) return null;
        var article = Request.Query[ArticleQuery].ToString() is { Length: > 0 } id && Tid.IsValid(id) && await Post.Get(id, ct) is { } post
                      && post.AuthorId == personId && post.ParentId is null
            ? post
            : null;
        return new ConnectionsPage.Failed(
            (article is null ? "Nothing changed." : "“" + article.Title + "” didn’t go out.") + " " + PublishController.Why(code),
            article is null ? null : address + AtprotoAdapter.Path(article));
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

    /// <summary>The account as its participant reads it; <paramref name="remains"/> says a copy of theirs is still out of
    /// an article that should not be, which the grant, where it is needed, comes back to take down.</summary>
    private static ConnectionsPage.Account Account(Connection? connection, string handle, string address, bool remains)
    {
        if (connection is null)
            return new("@" + handle, "none", "Your public articles can go out on Bluesky under your own account. Each one offers it, and Bluesky is asked for permission the first time you choose it.",
                null, null, null, null);
        if (connection.State == ConnectionState.NotNow)
            return new("@" + handle, "not-now", "You said not now to publishing on Bluesky, so your articles don't offer it.",
                null, null, null, Form.Posting(address + OfferAgainPath));
        if (connection.NeedsGrant)
            return new("@" + handle, "grant", Syndication.Ungranted + (remains ? " Grant it again to take down what is still public there." : " Grant it again to go on publishing."),
                connection.Since, null, Form.Posting(address + PublishController.GrantPath), null);
        if (connection.Trouble is { } trouble)
            return new("@" + handle, "trouble", trouble, connection.Since, connection.RetryAt, null, null);
        return new("@" + handle, "publishing", "Your public articles go out on Bluesky as @" + handle + ", and follow your edits and removals.",
            connection.Since, null, null, null);
    }

    /// <summary>An article's state in one word, the same here, on its page and in its JSON.</summary>
    public static string Word(ArticleState state) => state switch
    {
        ArticleState.NeedsGrant => "grant",
        ArticleState.EditedByOthers => "edited",
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
        ArticleState.EditedByOthers => "Edited by someone else; your copy stays as you wrote it",
        ArticleState.Remains => "Still public on Bluesky, though it no longer is here. " + Publishing.Undoing(connection),
        ArticleState.Held => "Not published, since it isn't public here",
        ArticleState.Earlier => "Written as your earlier Bluesky account; it and any copy of it stay with that account",
        _ => "Retracted"
    };
}
