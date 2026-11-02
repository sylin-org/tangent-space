using System.Globalization;
using System.Security.Claims;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Render;

namespace Tangent.Posts;

/// <summary>What the write routes of a Post share. A write comes as a form a page posted, as JSON an agent sent or as a
/// tool's call, to the same address and the same typed input, and is answered in kind: a form is sent on to the Post's
/// address (<c>303</c>) or, refused, reads its page again with the reason inline; a JSON request reads the Post with its
/// <c>can</c>, or a problem carrying the gate. Refusals answer by their kind, in one place (<see cref="Refused"/>). A
/// request a session or a bearer authenticated answers to <see cref="FormToken.Validate"/> before its address is resolved;
/// one that authenticates nobody holds no credential a form could borrow, and its command refuses it on its own
/// terms.</summary>
public static class PostWrites
{
    /// <summary>The most bytes a write request's body holds: a body at its limit, escaped as a form escapes it.</summary>
    public const long RequestLimit = 1024 * 1024;

    public const string IdField = "id";
    public const string TitleField = "title";
    public const string BodyField = "body";
    public const string KindField = "kind";
    public const string ParentField = "parent";
    public const string VersionField = "version";
    public const string CoverField = "cover";

    /// <summary>The refusal of a kind that is not one of <see cref="PostKind"/>'s words.</summary>
    public const string KindRule = "A Topic's kind is topic or article.";

    /// <summary>The refusal of an edit that names no version.</summary>
    public const string VersionRule = "An edit carries the version of the Post its editor read, as a whole number.";

    /// <summary>Where a form shows a refusal: under a field of what was sent, above what was sent as a whole, or as a gate,
    /// a verb the sender does not hold.</summary>
    public enum About
    {
        Title,
        Body,
        Sent,
        Gate
    }

    /// <summary>A title as a form sent it: on one line, since an article's title field wraps as the title does.</summary>
    public static string? Title(string? sent) => sent?.ReplaceLineEndings(" ");

    public static string? PersonId(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;

    /// <summary>Whether the sender reads JSON: it sent a JSON body, or its <c>Accept</c> names JSON and not HTML.</summary>
    public static bool WantsJson(HttpRequest request)
    {
        if (request.HasJsonContentType()) return true;
        var accept = request.GetTypedHeaders().Accept;
        return accept.Any(type => string.Equals(type.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(type.Suffix.Value, "json", StringComparison.OrdinalIgnoreCase))
               && !accept.Any(type => string.Equals(type.MediaType.Value, "text/html", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A write route's address as it stands for the reader: its Tangent, the rows what the reader holds there is
    /// computed from, and the Topic's root and the Post the address names, where it names them.</summary>
    public sealed record Located(Tangents.Tangent Tangent, Capabilities.Rows Rows, Post? Root, Post? Post)
    {
        /// <summary>What the reader holds on the Post the address names, a reply judged by its Topic's root; on the
        /// Tangent where the address names no Post.</summary>
        public Capabilities Capabilities => Rows.For(Post, Post is { ParentId: not null } ? Root : null);
    }

    /// <summary>Resolves a write route's address in the Topic page's order, before anything is judged of the request
    /// itself: the Tangent's row first, so an address no Tangent has reads no overwrites; capability's rows, once; the
    /// gate of the place or the Tangent, the same for every address under it, so a visitor of a private place reads one
    /// sign-in whether an address holds something or nothing; then the missing resource's answer for a Tangent no row
    /// has, a slug no Topic of it has, or an Id no Post of that Topic has. Answers the address found, or what it answers
    /// instead. The reads are the Topic page's: the Tangent, capability's rows, the root by slug and the Post by Id.</summary>
    public static async Task<(Located? Found, IActionResult? Unseen)> Locate(HttpContext http, string? personId, string tangent, string? topic, string? post,
        CancellationToken ct)
    {
        var row = Slug.IsValid(tangent) ? await Tangents.Tangent.Get(tangent, ct) : null;
        var rows = await Capabilities.LoadRows(personId, row?.Id, ct);
        var here = rows.For(null);
        if (!here.Exists) return (null, Unseen(http, here.Gate(Bit.View)));
        if (row is null) return (null, Absent());
        Post? root = null;
        if (topic is not null && (!Slug.IsValid(topic) || (root = await TopicWindow.Topic(row.Id, topic, ct)) is null)) return (null, Absent());
        Post? named = null;
        if (post is not null)
        {
            named = root is null || !Tid.IsValid(post) ? null : post == root.Id ? root : await Post.Get(post, ct);
            if (named is null || named.TopicId != root!.Id) return (null, Absent());
        }

        return (new Located(row, rows, root, named), null);
    }

    /// <summary>The kind a start names: null for none, the kind for one of its words, and false in <paramref name="valid"/>
    /// for anything else.</summary>
    public static PostKind? Kind(string? word, out bool valid)
    {
        valid = true;
        if (string.IsNullOrEmpty(word)) return null;
        foreach (var kind in Enum.GetValues<PostKind>())
        {
            if (string.Equals(Word(kind), word, StringComparison.OrdinalIgnoreCase)) return kind;
        }

        valid = false;
        return null;
    }

    public static string Word(PostKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>The body a write route hands its command: written as its sender wrote it, each typed handle of a
    /// participant here their mention, and judged (<see cref="PostBody.Written"/>) only where the person holds the verb
    /// the command needs here, so a request its gate will refuse never pays for a parse or a read; left unjudged
    /// otherwise.</summary>
    public static async Task<PostBody> Body(string? sent, bool mayWrite)
    {
        var text = sent ?? "";
        return mayWrite ? await PostBody.Written(text, CancellationToken.None) : PostBody.Unjudged(text);
    }

    /// <summary>The Id a write that makes a Post goes by: the one it sent; else the one its sender's words name
    /// (<see cref="Tid.Derived"/>), from who sent them, where they go (<paramref name="where"/>) and the words themselves.
    /// The words name it for a retry window and the window before, so a retry whose answer was lost finds the Post it
    /// wrote whichever window it falls in; later, the same words name a new one. An Id already holding anything else, the
    /// same Post edited or removed since among it, is no retry of it, and a TID is minted instead. A Topic's order is its
    /// sequence, which the command takes in its hold, so the time an Id reads says nothing of where the Post stands. One
    /// keyed read of each window's Id.</summary>
    public static async Task<string> IdFor(HttpContext http, string? sent, string? personId, PostBody body, params string?[] where)
    {
        if (sent is { Length: > 0 }) return sent;
        var now = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var words = string.Join('\0', [personId, .. where, body.SentHash]);
        foreach (var at in new[] { now, now - Tid.RetryWindow })
        {
            var id = Tid.Derived(words, at);
            if (await Post.Get(id, http.RequestAborted) is { } row)
                return row.AuthorId == personId && row.SentHash == body.SentHash ? id : Tid.Mint(now);
        }

        return Tid.Derived(words, now);
    }

    /// <summary>The form that replies in the Topic at <paramref name="topicAddress"/>: a TID minted for it, the body, and
    /// the Post it answers, <paramref name="parent"/>, or the Topic where none is named.</summary>
    public static Form ReplyForm(string topicAddress, string? parent, FormToken token) => Form.Posting(topicAddress, token,
        Form.Minted(IdField, Tid.Mint(DateTimeOffset.UtcNow)),
        new Form.Field(BodyField, Holds.ReplyBody),
        new Form.Field(ParentField, Holds.Parent, parent ?? ""));

    /// <summary>The form that starts a Topic at <paramref name="action"/>, a Tangent's, whose default kind is
    /// <paramref name="kind"/>: a TID minted for it, the title, the body and the kind.</summary>
    public static Form StartForm(string action, PostKind kind, FormToken token)
    {
        Form.Field[] fields =
        [
            Form.Minted(IdField, Tid.Mint(DateTimeOffset.UtcNow)),
            new Form.Field(TitleField, Holds.TopicTitle),
            new Form.Field(BodyField, Holds.TopicBody),
            new Form.Field(KindField, $"{Holds.Kind} Here, {Word(kind)}.")
        ];
        return Form.Posting(action, token, kind == PostKind.Article ? [.. fields, new Form.Field(CoverField, Holds.Cover)] : fields);
    }

    /// <summary>The form that brings an image in for someone writing at <paramref name="tangentAddress"/>.</summary>
    public static Form MediaForm(string tangentAddress, FormToken token) => Form.Posting(tangentAddress + "/" + NewMediaController.Segment, token,
        new Form.Field(NewMediaController.FileField, Holds.BroughtImage));

    /// <summary>The form that edits a Post at <paramref name="action"/> from the <paramref name="version"/> its editor read:
    /// the version, the title on a Topic's <paramref name="root"/>, and the body, which may be empty on a root taken on a
    /// tangent.</summary>
    public static Form EditForm(string action, long version, bool root, bool bodyOptional, FormToken token, string? cover = null, bool article = false)
    {
        var body = new Form.Field(BodyField, Holds.EditBody + (bodyOptional ? Holds.EditBodyOptional : ""));
        var from = new Form.Field(VersionField, Holds.EditVersion, version.ToString(CultureInfo.InvariantCulture));
        if (article)
            return Form.Posting(action, token, from, new Form.Field(TitleField, Holds.EditTitle), body, new Form.Field(CoverField, Holds.EditCover, cover ?? ""));
        return root
            ? Form.Posting(action, token, from, new Form.Field(TitleField, Holds.EditTitle), body)
            : Form.Posting(action, token, from, body);
    }

    /// <summary>The form that removes a Post at <paramref name="action"/>: nothing but its token.</summary>
    public static Form RemoveForm(string action, FormToken token) => Form.Posting(action, token);

    /// <summary>The form that reports a Post at <paramref name="action"/>, with the TID <paramref name="minted"/> for it, and
    /// the reason.</summary>
    public static Form ReportForm(string action, string minted, FormToken token) => Form.Posting(action, token,
        Form.Minted(IdField, minted),
        new Form.Field(BodyField, Holds.Reason));

    public static string TangentAddress(string baseUrl, string tangent) => baseUrl + "/t/" + Uri.EscapeDataString(tangent);

    public static string TopicAddress(string baseUrl, string tangent, string topic)
        => TangentAddress(baseUrl, tangent) + "/" + Uri.EscapeDataString(topic);

    /// <summary>A Post's address, which the Topic answers opened around it: <c>/t/{tangent}/{topic}/{post}</c>.</summary>
    public static string Address(string baseUrl, string tangent, string topic, string post)
        => TopicAddress(baseUrl, tangent, topic) + "/" + Uri.EscapeDataString(post);

    /// <summary>Where a form shows a refusal: its kind says a gate; the title's and body's rules name their field; anything
    /// else is about what was sent as a whole.</summary>
    public static About Concerning(Reason reason) => reason.Kind == ReasonKind.Gated ? About.Gate : reason.Text switch
    {
        Post.NeedsTitle or Post.TitleTooLong => About.Title,
        Post.NeedsBody or Post.BodyTooLong or Post.BodyHasNul or Post.BodyRendersTooLong or Post.TooManyMentions or Post.TooManyImages or Post.BodyTooIntricate => About.Body,
        _ => About.Sent
    };

    /// <summary>A write that committed: a form is sent on to the Post's address; a JSON request reads the Post with the
    /// sender's <c>can</c> on it — a reply's judged by its Topic's <paramref name="root"/> — created (<c>201</c>) or
    /// changed (<c>200</c>), with that address as its <c>Location</c>.</summary>
    public static async Task<IActionResult> Written(HttpContext http, Post post, Post root, string address, string? personId, bool created,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Location = address;
        if (!WantsJson(http.Request)) return new StatusCodeResult(StatusCodes.Status303SeeOther);
        var capabilities = (await Capabilities.LoadRows(personId, post.TangentId, ct)).For(post, post.ParentId is null ? null : root);
        return new ObjectResult(WrittenPost.Of(post, address, capabilities.Can))
        {
            StatusCode = created ? StatusCodes.Status201Created : StatusCodes.Status200OK,
            ContentTypes = { "application/json" }
        };
    }

    /// <summary>What an address answers that holds nothing the reader may see: the one answer of a missing resource.</summary>
    public static IActionResult Absent()
        => new Problem(StatusCodes.Status404NotFound, "Not found", "Nothing answers at this address.", Capabilities.Absent.Text).ToResult();

    /// <summary>What a reader without <see cref="Bit.View"/> where they ask receives: the missing resource's answer where
    /// it is absent to them, otherwise the one step between them and it.</summary>
    public static IActionResult Unseen(HttpContext http, Reason? gate) => gate is null ? Absent() : Refused(http, gate);

    public static IActionResult TokenRefused()
        => new Problem(StatusCodes.Status400BadRequest, "Open the form again",
            "This form’s token is missing or has expired, so nothing was written.").ToResult();

    /// <summary>The one answer to a command that is not done, as a page or as JSON by the request's <c>Accept</c>, through
    /// the shell and in the words of <see cref="Refused"/>.</summary>
    public static IActionResult Answer<T>(HttpContext http, Outcome<T> outcome) => outcome switch
    {
        Outcome<T>.Refused { Reason: var reason } => Refused(http, reason),
        Outcome<T>.Failed failed => Failed(http, failed.Landed, failed.Cause),
        _ => throw new ArgumentException("A command that is done answers as its route says.", nameof(outcome))
    };

    /// <summary>The problem a refusal answers as, by its kind: absent <c>404</c> in one sentence; gated <c>401</c> to sign
    /// in and <c>403</c> otherwise, with its heading, its word as <c>gate</c>, the sentence of a rule that has no word, and
    /// its next step as <c>next</c>; conflict <c>409</c>; anything else <c>400</c>. Where a gate means something more
    /// specific at this address, <paramref name="saying"/> and <paramref name="heading"/> say it.</summary>
    public static IActionResult Refused(HttpContext http, Reason reason, string? saying = null, string? heading = null) => (reason.Kind switch
    {
        ReasonKind.Absent => new Problem(StatusCodes.Status404NotFound, "Not found", reason.Said, Capabilities.Absent.Text),
        ReasonKind.Gated => new Problem(Status(reason), heading ?? Heading(reason), saying ?? reason.Text,
            reason.Word ?? reason.Text, reason.Next is { } next ? Next(http, next) : null),
        ReasonKind.Conflict => new Problem(StatusCodes.Status409Conflict, "Nothing changed", reason.Text),
        _ => new Problem(StatusCodes.Status400BadRequest, "Not written", reason.Text)
    }).ToResult();

    /// <summary>What a person reads above a gate's sentence: its own heading, where it declares one.</summary>
    private static string Heading(Reason gate) => gate.Heading ?? (gate.Word is null ? "Not yours to change" : "Not open to you");

    /// <summary>The status a refusal answers with, by its kind.</summary>
    public static int Status(Reason reason) => reason.Kind switch
    {
        ReasonKind.Absent => StatusCodes.Status404NotFound,
        ReasonKind.Gated => reason.Word is { } word ? Status(word) : StatusCodes.Status403Forbidden,
        ReasonKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };

    /// <summary>The status a page answers with whose reader meets the gate of this word.</summary>
    public static int Status(string gate) => gate == Capabilities.SignIn.Word ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;

    /// <summary>What a command that could not finish answers: it never ran, <c>503</c>, and the sender may send it again
    /// shortly, or it failed, <c>500</c>, saying what it left and nothing of why, which is logged.</summary>
    private static IActionResult Failed(HttpContext http, DataCommitOutcome landed, Exception cause)
    {
        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PostWrites)).LogError(cause, "A command failed at {Path}", http.Request.Path);
        if (landed == DataCommitOutcome.NotDispatched)
        {
            http.Response.Headers.RetryAfter = "1";
            return new Problem(StatusCodes.Status503ServiceUnavailable, "Try again", "This place is busy, so nothing was written; your words are safe to send again.").ToResult();
        }

        return new Problem(StatusCodes.Status500InternalServerError, "Not written",
            landed == DataCommitOutcome.Unknown ? "This may or may not have been written; look before sending it again." : "This failed, and nothing was written.").ToResult();
    }

    /// <summary>A gate's next step under the base URL; the address a sign-in comes back to is that of a <c>GET</c>.</summary>
    private static string Next(HttpContext http, string next)
    {
        var address = http.RequestServices.GetRequiredService<KoanWebBaseUrl>().Resolve(http) + next;
        return HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)
            ? address + "?return=" + Uri.EscapeDataString(http.Request.PathBase + http.Request.Path + http.Request.QueryString)
            : address;
    }

    /// <summary>Who is signed in, as the shell's account menu shows them, signing out back to <paramref name="back"/>, a
    /// path under the base URL, with the place's settings where they hold <see cref="Bit.ManageSpace"/>,
    /// <see cref="Bit.ManageStanding"/> or <see cref="Bit.ManageRoles"/>, and how many of their notifications they have
    /// not seen; null for a visitor.</summary>
    public static async Task<Who?> Reader(string? personId, Capabilities capabilities, string baseUrl, string back, CancellationToken ct)
    {
        if (personId is null) return null;
        var participant = await Participant.Get(personId, ct);
        var handle = participant?.Handle is { Length: > 0 } stored ? stored : personId;
        var name = participant?.Profile.DisplayName is { Length: > 0 } displayName ? displayName : handle;
        return new Who(personId, name, handle, Roles.Word(capabilities.RoleIds), participant?.Profile.Avatar,
            baseUrl + AuthConstants.Routes.Logout + "?return=" + Uri.EscapeDataString(back), await SettingsFor(personId, baseUrl, ct),
            await Events.Notifications.Count(personId, baseUrl, ct));
    }

    /// <summary>Where the place's settings open under <paramref name="baseUrl"/> for a participant who holds
    /// <see cref="Bit.ManageSpace"/>, <see cref="Bit.ManageStanding"/> or <see cref="Bit.ManageRoles"/> in the place, judged in the place rather than in
    /// any one Tangent (<see cref="Space.TangentSpaceSettingsController.Entry"/>); null for anyone else.</summary>
    public static async Task<string?> SettingsFor(string personId, string baseUrl, CancellationToken ct)
        => Space.TangentSpaceSettingsController.Entry(await Capabilities.Load(personId, null, null, ct), baseUrl);
}
