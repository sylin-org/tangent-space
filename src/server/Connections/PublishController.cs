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
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Connections;

/// <summary>Publishing an article under its author's own account. <c>POST /t/{tangent}/{topic}/publish</c>, for the
/// article's author alone and only while it is public: where their account already lets the place write, their
/// Connection publishes from now on and the article waits to be published, and the answer goes back to the article;
/// where it does not, the answer is atproto's request for that permission, for this account alone, which comes back to
/// <see cref="GrantedPath"/> with a sealed note of which article, and publishes it there. Permission is asked here,
/// the first time someone chooses to publish, never at sign-in. <see cref="NotNowPath"/> answers the first offer not
/// now.</summary>
public sealed class PublishController(KoanWebBaseUrl baseUrl, AtprotoAdapter atproto, EventStream stream, Nudge nudge,
    IDataProtectionProvider protection) : ControllerBase
{
    public const string Segment = "publish";

    public const string Route = "/t/{tangent}/{topic}/" + Segment;

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
    public static readonly Reason OnlyPublic = Reason.Gated("private", "Only a public article is published: members alone read this one.");
    public static readonly Reason GrantExpired = Reason.Invalid("That permission came back too late or for someone else; publish the article again.");

    private const string GrantPurpose = "Tangent.Connections.Grant.v1";

    [HttpPost(Route)]
    [Tool(Name = "publish", Destructive = false, Idempotent = true, OpenWorld = true)]
    [Needs(Bit.View, Participant = true)]
    [Description("Publish your own public article under your own atproto account: a site.standard.document in your repository and a Bluesky post that embeds it, kept in step with your edits and removed with it. The first time, the answer is atproto's request for permission to write to your repository, to open as its Location; after that, your new public articles publish on their own.")]
    public async Task<IActionResult> Publish([FromRoute, Description(Holds.Tangent)] string tangent, [FromRoute, Description(Holds.Topic)] string topic,
        CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var here = Here.Of(HttpContext);
        var personId = here.PersonId!;
        var root = here.Root!;
        if (await Refusal(root, personId, here.Rows.Space, ct) is { } refusal) return Answers.Refused(HttpContext, refusal);

        if (!await atproto.MayWrite(personId, here.Rows.Space, ct))
            return Asked(personId, root.Id, PostWrites.TopicAddress(baseUrl.PathBase, root.TangentId, root.Slug ?? root.Id), here.Rows.Space);
        return await Chosen(personId, root, baseUrl.Resolve(HttpContext), ct);
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
        return Asked(personId, null, baseUrl.PathBase + ConnectionsController.Path, space);
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

    /// <summary>atproto's request for permission to write to <paramref name="personId"/>'s repository, asking that
    /// account alone, coming back to <see cref="GrantedPath"/> with the article sealed in, or to <paramref name="failed"/>
    /// where they refuse it.</summary>
    private ChallengeResult Asked(string personId, string? postId, string failed, TangentSpace space)
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = baseUrl.PathBase + GrantedPath + "?" + TokenParameter + "=" + Uri.EscapeDataString(Seal(personId, postId ?? ""))
        };
        properties.Items[AuthConstants.Items.FailurePath] = failed;
        AtprotoChallenge.WithScopes(properties, [.. AtprotoAdapter.ScopesFor(personId, space)]);
        return Challenge(AtprotoChallenge.ForAccount(properties, personId), "atproto");
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

    /// <summary>Why <paramref name="personId"/> may not publish <paramref name="root"/>, or null where they may.</summary>
    private static async Task<Reason?> Refusal(Post root, string personId, TangentSpace space, CancellationToken ct)
    {
        if (root.ParentId is not null || root.Kind != PostKind.Article) return OnlyArticles;
        if (root.AuthorId != personId) return OnlyTheAuthor;
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
