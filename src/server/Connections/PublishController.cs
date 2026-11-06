using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Connections;

/// <summary>Publishing an article under its author's own account. <c>POST /t/{tangent}/{topic}/publish</c>, for the
/// article's author alone and only while it is public: where their account already lets the place write, their
/// Connection publishes from now on and the article waits to be published, and the answer goes back to the article;
/// where it does not, the answer is atproto's request for that permission, for this account alone, which comes back to
/// <see cref="GrantedPath"/> with a sealed note of which article, and publishes it there. A permission not given — declined
/// on Bluesky's page, or a request that could not start — comes back to the Connections page, which says why
/// (<see cref="Why"/>); a JSON or tool caller whose request could not start is refused with <see cref="NotGranted"/>'s
/// word and why instead. Permission is asked here, the first time someone chooses to publish, never at sign-in.
/// <see cref="NotNowPath"/> answers the first offer not now. <c>POST /t/{tangent}/{topic}/retract</c>, for the same author
/// under the same conditions, takes the article's copy off Bluesky while it stays here, until they publish it again.</summary>
public sealed class PublishController(KoanWebBaseUrl baseUrl, AtprotoAdapter atproto, EventStream stream, Nudge nudge,
    IDataProtectionProvider protection) : ControllerBase
{
    public const string Segment = "publish";

    public const string Route = "/t/{tangent}/{topic}/" + Segment;

    public const string RetractSegment = "retract";

    public const string RetractRoute = "/t/{tangent}/{topic}/" + RetractSegment;

    /// <summary>Where a granted permission comes back, with <see cref="TokenParameter"/>.</summary>
    public const string GrantedPath = "/settings/connections/granted";

    public const string TokenParameter = "article";

    public const string NotNowPath = "/settings/connections/not-now";

    /// <summary>Where a Connection that lost its permission asks for it again.</summary>
    public const string GrantPath = "/settings/connections/grant";

    /// <summary>The field of the not-now form naming the page it comes back to.</summary>
    public const string BackField = "back";

    /// <summary>How long a permission request may take before the article it names is forgotten.</summary>
    public static readonly TimeSpan GrantLifetime = TimeSpan.FromMinutes(30);

    public static readonly Reason OnlyArticles = Reason.Invalid("Only an article is published under your own account.");
    public static readonly Reason OnlyTheAuthor = Reason.Gated("not yours", "Only its author publishes an article under their own account.");

    /// <summary>The refusal an article meets that its author wrote as a Bluesky account they are no longer bound to.</summary>
    public static readonly Reason EarlierAccount = Reason.Conflict("earlier account", Publishing.EarlierSentence);
    public static readonly Reason OnlyPublic = Reason.Gated("private", "Only a public article is published: members alone read this one.");
    public static readonly Reason GrantExpired = Reason.Invalid("That permission came back too late or for someone else; publish the article again.");

    /// <summary>The refusal a take-down meets where nothing of the article went out or waits to.</summary>
    public static readonly Reason NotOut = Reason.Conflict("not out", "Nothing of this article is on Bluesky.");

    /// <summary>The word a permission request that could not start is refused with.</summary>
    public const string NotGranted = "not-granted";

    /// <summary>Why a permission was not given, in a sentence, from Koan's code for it; an unknown code reads as one
    /// Bluesky did not confirm.</summary>
    public static string Why(string? code) => code switch
    {
        _ when code == SignInFailureReturn.Code(SignInFailure.AccessDenied) => "You declined the permission on Bluesky’s page.",
        _ when code == SignInFailureReturn.Code(SignInFailure.ProviderUnreachable) => "Your account’s provider couldn’t be reached; try again in a moment.",
        _ when code == SignInFailureReturn.Code(SignInFailure.HandleUnresolved) => "Your account couldn’t be found on the network just now; try again in a moment.",
        _ when code == SignInFailureReturn.Code(SignInFailure.CallbackMismatch) => "Bluesky’s answer came back without the request it began with; try again.",
        _ => "Bluesky didn’t confirm the permission; try again, then approve it there."
    };

    private const string GrantPurpose = "Tangent.Connections.Grant.v1";

    [HttpPost(Route)]
    [Tool(Name = "publish", Destructive = false, Idempotent = true, OpenWorld = true)]
    [Needs(Bit.View, Participant = true)]
    [Description("Publish your own public article under your own atproto account: a site.standard.document in your repository and a Bluesky post that embeds it, kept in step with your edits and removed with it. The first time, the answer is atproto's request for permission to write to your repository, for you to open as its Location in a browser, or, where it cannot be asked, a refusal not-granted saying why; after that, your new public articles publish on their own, unless you stopped that with stop_publishing.")]
    public async Task<IActionResult> Publish([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var here = Here.Of(HttpContext);
        var personId = here.PersonId!;
        var root = here.Root!;
        if (await Refusal(root, personId, here.Rows.Space, ct) is { } refusal) return Answers.Refused(HttpContext, refusal);

        if (!await atproto.MayWrite(personId, root.AuthorDid, here.Rows.Space, ct))
            return Asked(personId, root.AuthorDid, root.Id, ConnectionsController.Path + "?" + ConnectionsController.ArticleQuery + "=" + root.Id, here.Rows.Space);
        return await Chosen(personId, root, baseUrl.Resolve(HttpContext), ct);
    }

    [HttpPost(RetractRoute)]
    [Tool(Name = "retract", Destructive = true, Idempotent = true, OpenWorld = true)]
    [Needs(Bit.View, Participant = true)]
    [Description("Take your own public article off Bluesky while it stays here: its site.standard.document and the Bluesky post that embeds it are deleted from your repository, with the likes, reposts and replies there, and nothing of it goes out again, edits included, until you publish it again. read_connections says retracting until it is down, then retracted.")]
    public async Task<IActionResult> Retract([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        [FromForm(Name = BackField), Description(Holds.RetractBack)] string? back, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var here = Here.Of(HttpContext);
        var personId = here.PersonId!;
        var root = here.Root!;
        if (await Refusal(root, personId, here.Rows.Space, ct) is { } refusal) return Answers.Refused(HttpContext, refusal);

        var outcome = await new Withhold(personId, root.Id, DateTimeOffset.UtcNow).Send(ct);
        if (outcome is not Outcome<Receipt?>.Done { Result: var receipt }) return Answers.NotDone(HttpContext, outcome);
        if (receipt is null) return Answers.Refused(HttpContext, NotOut);
        await nudge.Wake(personId);
        var address = baseUrl.Resolve(HttpContext);
        var article = address + AtprotoAdapter.Path(root);
        if (PostWrites.WantsJson(Request))
            return Accepted(new { state = ConnectionsController.Word(receipt.IsLive() ? ArticleState.Retracting : ArticleState.Retracted), article,
                connections = address + ConnectionsController.Path });
        Response.Headers.Location = back == ConnectionsController.Path ? address + back : article;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    /// <summary>Where atproto sends someone back once they granted the place permission to write to their repository.</summary>
    [HttpGet(GrantedPath)]
    public async Task<IActionResult> Granted([FromQuery(Name = TokenParameter)] string? article, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (PostWrites.PersonId(User) is not { } personId || Opened(article) is not { } sealedFor || sealedFor.PersonId != personId)
            return Answers.Refused(HttpContext, GrantExpired);
        var address = baseUrl.Resolve(HttpContext);
        if (sealedFor.PostId.Length == 0) return await Chosen(personId, null, address, ct);
        var root = await Post.Get(sealedFor.PostId, ct);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        if (root is null) return Answers.Refused(HttpContext, Capabilities.Absent);
        if (await Refusal(root, personId, space, ct) is { } refusal) return Answers.Refused(HttpContext, refusal);
        return await Chosen(personId, root, address, ct);
    }

    /// <summary>Asks atproto again for the permission a Connection lost, with no article named, coming back to the
    /// Connections page.</summary>
    [HttpPost(GrantPath)]
    public async Task<IActionResult> Grant(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (PostWrites.PersonId(User) is not { } personId) return Answers.Refused(HttpContext, Capabilities.SignIn);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        return Asked(personId, await Participant.DidOf(personId, ct), null, ConnectionsController.Path, space);
    }

    /// <summary>Not now, to the first offer to publish under one's own account: no article offers it again until the
    /// Connections page brings it back.</summary>
    [HttpPost(NotNowPath)]
    public async Task<IActionResult> NotNow([FromForm(Name = BackField)] string? back, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (PostWrites.PersonId(User) is not { } personId) return Answers.Refused(HttpContext, Capabilities.SignIn);
        var outcome = await new SayNotNow(personId, DateTimeOffset.UtcNow).Send(ct);
        if (outcome is not Outcome<Connection>.Done) return Answers.NotDone(HttpContext, outcome);
        nudge.Changed();
        var address = baseUrl.Resolve(HttpContext);
        if (PostWrites.WantsJson(Request)) return Ok(new { connections = address + ConnectionsController.Path });
        Response.Headers.Location = address + (back is { } path && IsArticlePath(path) ? path : ConnectionsController.Path);
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    /// <summary>The path under the base URL a not-now form comes back to: the article's own.</summary>
    public static string Back(string articleAddress, string baseAddress) => articleAddress[baseAddress.Length..];

    /// <summary>atproto's request for permission to write to the repository of <paramref name="did"/>, the account
    /// <paramref name="personId"/> is bound to, asking that account alone, coming back to <see cref="GrantedPath"/> with the article sealed in, or to <paramref name="failed"/>,
    /// a path Koan places under the path base, where it is not given; for a JSON or tool caller, a request that cannot
    /// start is a refusal (<see cref="Unstarted"/>).</summary>
    private IActionResult Asked(string personId, string did, string? postId, string failed, TangentSpace space)
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = baseUrl.PathBase + GrantedPath + "?" + TokenParameter + "=" + Uri.EscapeDataString(Seal(personId, postId ?? ""))
        };
        properties.Items[AuthConstants.Items.FailurePath] = failed;
        AtprotoChallenge.WithScopes(properties, [.. AtprotoAdapter.ScopesFor(personId, space)]);
        var challenge = Challenge(AtprotoChallenge.ForAccount(properties, did), "atproto");
        return PostWrites.WantsJson(Request) ? new Unstarted(challenge) : challenge;
    }

    /// <summary>atproto's request for a JSON or tool caller: where it starts, the request itself, to open as its
    /// <c>Location</c>; where it cannot, and Koan sends the caller to the failure landing with its code, a refusal with
    /// <see cref="NotGranted"/> and why, so nothing reads it as done.</summary>
    private sealed class Unstarted(ChallengeResult challenge) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            await challenge.ExecuteResultAsync(context);
            var http = context.HttpContext;
            var location = http.Response.Headers.Location.ToString();
            if (http.Response.HasStarted || !location.StartsWith('/')) return;
            var query = location.IndexOf('?') is var at and >= 0 ? location[at..] : "";
            if (!Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query).TryGetValue(SignInFailureReturn.CodeParameter, out var code)) return;
            http.Response.Clear();
            http.Response.Headers.CacheControl = "no-store";
            await Answers.Refused(http, Reason.Gated(NotGranted, "Bluesky’s permission couldn’t be asked. " + Why(code.ToString()))).ExecuteResultAsync(context);
        }
    }

    private async Task<IActionResult> Chosen(string personId, Post? root, string address, CancellationToken ct)
    {
        var outcome = await new ChooseToPublish(personId, root?.Id, root?.TangentId, root?.Version ?? 0, await stream.Head(ct), DateTimeOffset.UtcNow).Send(ct);
        if (outcome is not Outcome<Connection>.Done) return Answers.NotDone(HttpContext, outcome);
        nudge.Changed();
        await nudge.Wake(personId);
        var article = root is null ? address + ConnectionsController.Path : address + AtprotoAdapter.Path(root);
        if (PostWrites.WantsJson(Request))
            return Accepted(new { state = "publishing", article, connections = address + ConnectionsController.Path });
        Response.Headers.Location = article;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    /// <summary>Why <paramref name="personId"/> may not publish <paramref name="root"/>, or take it down, or null where they
    /// may.</summary>
    private static async Task<Reason?> Refusal(Post root, string personId, TangentSpace space, CancellationToken ct)
    {
        if (root.ParentId is not null || root.Kind != PostKind.Article) return OnlyArticles;
        if (root.AuthorId != personId) return OnlyTheAuthor;
        if (root.AuthorDid != await Participant.DidOf(personId, ct)) return EarlierAccount;
        if (root.Removed) return Capabilities.Removed;
        return await Publishing.Open(space, root.TangentId, ct) ? null : OnlyPublic;
    }

    private static bool IsArticlePath(string path)
        => path.StartsWith("/t/", StringComparison.Ordinal) && !path.Contains("//", StringComparison.Ordinal) && !path.Contains('\\')
           && Uri.IsWellFormedUriString(path, UriKind.Relative);

    private string Seal(string personId, string postId)
        => protection.CreateProtector(GrantPurpose).ToTimeLimitedDataProtector().Protect(personId + "\n" + postId, GrantLifetime);

    private (string PersonId, string PostId)? Opened(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            return protection.CreateProtector(GrantPurpose).ToTimeLimitedDataProtector().Unprotect(token).Split('\n') is [var person, var post]
                ? (person, post) : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
