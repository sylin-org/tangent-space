using System.Globalization;
using System.Security.Claims;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Render;

namespace Tangent.Posts;

/// <summary>What the write routes of a Post share. A write comes as a form a page posted, as JSON an agent sent or as a
/// tool's call, to the same address and the same typed input, and is answered in kind: a form is sent on to the Post's
/// address (<c>303</c>) or, refused, reads its page again with the reason inline; a JSON request reads the Post with its
/// <c>can</c>, or a problem carrying its word, and the field a value broke where its rule concerns one
/// (<see cref="Refused"/>). Refusals answer by their kind, in Koan's one mapping (<see cref="Answers"/>). A
/// write answers to its sender's session cookie; one a page of another site sent is refused before it reaches
/// here.</summary>
public static class PostWrites
{
    /// <summary>The most bytes a write request's body holds: a body at its limit, escaped as a form escapes it.</summary>
    public const long RequestLimit = 1024 * 1024;

    public const string IdField = "id";
    public const string TitleField = "title";
    public const string BodyField = "body";
    public const string ParentField = "parent";
    public const string VersionField = "version";
    public const string CoverField = "cover";
    public const string OriginField = "origin";


    /// <summary>The refusal of an edit that names no version.</summary>
    public static readonly Reason VersionRule = Reason.Invalid(VersionField, "An edit carries the version of the Post its editor read, as a whole number.");

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

    /// <summary>The field a form carries to say where it is sent on once its write is done.</summary>
    public const string ReturnField = "return";

    /// <summary>Where a form whose write is done is sent: the path of this place its <see cref="ReturnField"/> names, under
    /// <paramref name="baseAddress"/>, so a write offered on the door returns there; otherwise <paramref name="fallback"/>,
    /// the page the write belongs to. A path is one segment or more after a single slash, never another host's.</summary>
    public static string Onward(HttpContext http, string baseAddress, string fallback)
        => http.Request.HasFormContentType && http.Request.Form[ReturnField].ToString() is { Length: > 0 } path
           && path[0] == '/' && !path.StartsWith("//", StringComparison.Ordinal) && !path.Contains('\\') && !path.Any(char.IsControl)
            ? baseAddress + path
            : fallback;

    /// <summary>Whether the sender reads JSON: it sent a JSON body, or its <c>Accept</c> names JSON and not HTML.</summary>
    public static bool WantsJson(HttpRequest request)
    {
        if (request.HasJsonContentType()) return true;
        var accept = request.GetTypedHeaders().Accept;
        return accept.Any(type => string.Equals(type.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(type.Suffix.Value, "json", StringComparison.OrdinalIgnoreCase))
               && !accept.Any(type => string.Equals(type.MediaType.Value, "text/html", StringComparison.OrdinalIgnoreCase));
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

    /// <summary>The Id a write that makes a Post goes by: the one it sent, which makes its retry safe, or else a new
    /// TID, so the same words sent again make another Post.</summary>
    public static string Id(string? sent) => sent is { Length: > 0 } ? sent : Tid.Mint(DateTimeOffset.UtcNow);

    /// <summary>The form that replies in the Topic at <paramref name="topicAddress"/>: a TID minted for it, the body, and
    /// the Post it answers, <paramref name="parent"/>, or the Topic where none is named.</summary>
    public static Form ReplyForm(string topicAddress, string? parent) => Form.Posting(topicAddress,
        Form.Minted(IdField, Tid.Mint(DateTimeOffset.UtcNow)),
        new Form.Field(BodyField, Holds.ReplyBody),
        new Form.Field(ParentField, Holds.Parent, parent ?? ""));

    /// <summary>The form that starts a Topic at <paramref name="action"/>, a Tangent's, whose default kind
    /// <paramref name="kind"/> the Topic takes: a TID minted for it, the title and the body, the Post it takes on a tangent,
    /// <paramref name="origin"/> where the page names one, and an article's cover.</summary>
    public static Form StartForm(string action, PostKind kind, string? origin = null)
    {
        Form.Field[] fields =
        [
            Form.Minted(IdField, Tid.Mint(DateTimeOffset.UtcNow)),
            new Form.Field(TitleField, Holds.TopicTitle),
            new Form.Field(BodyField, Holds.TopicBody),
            .. origin is null ? Array.Empty<Form.Field>() : [new Form.Field(OriginField, Holds.TakenFrom, origin)],
            .. kind == PostKind.Article ? [new Form.Field(CoverField, Holds.Cover)] : Array.Empty<Form.Field>()
        ];
        return Form.Posting(action, fields);
    }

    /// <summary>The form that brings an image in for someone writing at <paramref name="tangentAddress"/>.</summary>
    public static Form MediaForm(string tangentAddress) => Form.Posting(tangentAddress + "/" + NewMediaController.Segment,
        new Form.Field(NewMediaController.FileField, Holds.BroughtImage));

    /// <summary>The form that edits a Post at <paramref name="action"/> from the <paramref name="version"/> its editor read:
    /// the version, the title on a Topic's <paramref name="root"/>, and the body, which may be empty on a root taken on a
    /// tangent.</summary>
    public static Form EditForm(string action, long version, bool root, bool bodyOptional, string? cover = null, bool article = false)
    {
        var body = new Form.Field(BodyField, Holds.EditBody + (bodyOptional ? Holds.EditBodyOptional : ""));
        var from = new Form.Field(VersionField, Holds.EditVersion, version.ToString(CultureInfo.InvariantCulture));
        if (article)
            return Form.Posting(action, from, new Form.Field(TitleField, Holds.EditTitle), body, new Form.Field(CoverField, Holds.EditCover, cover ?? ""));
        return root
            ? Form.Posting(action, from, new Form.Field(TitleField, Holds.EditTitle), body)
            : Form.Posting(action, from, body);
    }

    /// <summary>The form that removes <paramref name="post"/> at <paramref name="action"/>, which carries no field, confirmed
    /// in the page with what removing it does: its words go and a tombstone stays, and removing a Topic's root closes the
    /// Topic, whose replies stay. It opens behind <paramref name="opens"/>.</summary>
    public static Form RemoveForm(string action, Post post, string opens) => Form.Posting(action) with
    {
        Tool = "remove", Submit = "Remove", Opens = opens,
        Confirm = "Its words leave the page and a tombstone stays in their place" + (post.ParentId is not null ? "."
            : "; the " + Word(post.Kind) + " keeps its replies and takes no more.")
    };

    /// <summary>The form that reports a Post at <paramref name="action"/>, with the TID <paramref name="minted"/> for it, and
    /// the reason.</summary>
    public static Form ReportForm(string action, string minted) => Form.Posting(action,
        Form.Minted(IdField, minted),
        new Form.Field(BodyField, Holds.Reason, As: Form.Input.Area)
        {
            Label = "Anything the moderators should know? (optional)",
            Note = "Only the moderators read it, and its author is never told. It removes nothing by itself."
        }) with { Tool = "report", Submit = "Send the report", Busy = "Sending…", Key = "report-" + minted };

    public static string TangentAddress(string baseUrl, string tangent) => baseUrl + "/t/" + Uri.EscapeDataString(tangent);

    public static string TopicAddress(string baseUrl, string tangent, string topic)
        => TangentAddress(baseUrl, tangent) + "/" + Uri.EscapeDataString(topic);

    /// <summary>A Post's address, which the Topic answers opened around it: <c>/t/{tangent}/{topic}/{post}</c>.</summary>
    public static string Address(string baseUrl, string tangent, string topic, string post)
        => TopicAddress(baseUrl, tangent, topic) + "/" + Uri.EscapeDataString(post);

    /// <summary>Where a form shows a refusal: its kind says a gate; the title's and body's rules name their field; anything
    /// else is about what was sent as a whole.</summary>
    public static About Concerning(Reason reason) => reason.Kind == ReasonKind.Gated ? About.Gate : FieldOf(reason) switch
    {
        TitleField => About.Title,
        BodyField => About.Body,
        _ => About.Sent
    };

    /// <summary>The field of what was sent that <paramref name="reason"/> concerns, as a write's form names it: the one the
    /// reason names, else the title's, the body's, the Id's, the cover's or the parent's for their rules; null for a
    /// reason about what was sent as a whole.</summary>
    public static string? FieldOf(Reason reason) => reason.Field
        ?? (TitleRules.Contains(reason) ? TitleField
            : BodyRules.Contains(reason) ? BodyField
            : reason == Post.NeedsTid ? IdField
            : reason == Post.NoCover || reason == Post.CoverOnArticle ? CoverField
            : reason == Reply.NoParent ? ParentField
            : null);

    /// <summary>A write's refusal as a JSON request reads it: the problem of its reason, with an invalid value's sentence
    /// under the field it concerns (<see cref="FieldOf"/>) in <c>errors</c>.</summary>
    public static IActionResult Refused(HttpContext http, Reason reason)
        => Answers.Refused(http, reason is { Kind: ReasonKind.Invalid, Field: null } && FieldOf(reason) is { } field ? Reason.Invalid(field, reason.Text) : reason);

    private static readonly Reason[] TitleRules = [Post.NeedsTitle, Post.TitleTooLong];

    private static readonly Reason[] BodyRules =
        [Post.NeedsBody, Post.BodyTooLong, Post.BodyHasNul, Post.BodyRendersTooLong, Post.TooManyMentions, Post.TooManyImages, Post.BodyTooIntricate];

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

    /// <summary>Who is signed in, as the shell's account menu shows them, signing out back to <paramref name="back"/>, a
    /// path under the base URL, with the place's settings where they hold <see cref="Bit.ManageSpace"/>,
    /// <see cref="Bit.ManageStanding"/> or <see cref="Bit.ManageRoles"/>, and how many things are for them
    /// and new; null for a visitor.</summary>
    public static async Task<Who?> Reader(string? personId, Capabilities capabilities, string baseUrl, string back, CancellationToken ct)
    {
        if (personId is null) return null;
        var participant = await Participant.Get(personId, ct);
        return new Who(personId, participant?.ShownName() ?? personId, participant?.ShownHandle() ?? personId, Roles.Word(capabilities.RoleIds), participant?.Profile.Avatar,
            baseUrl + AuthConstants.Routes.Logout + "?return=" + Uri.EscapeDataString(back), await SettingsFor(personId, baseUrl, ct),
            await Events.Unread.BadgeFor(personId, baseUrl, ct));
    }

    /// <summary>Where the place's settings open under <paramref name="baseUrl"/> for a participant who holds
    /// <see cref="Bit.ManageSpace"/>, <see cref="Bit.ManageStanding"/> or <see cref="Bit.ManageRoles"/> in the place, judged in the place rather than in
    /// any one Tangent (<see cref="Space.TangentSpaceSettingsController.Entry"/>); null for anyone else.</summary>
    public static async Task<string?> SettingsFor(string personId, string baseUrl, CancellationToken ct)
        => Space.TangentSpaceSettingsController.Entry(await Capabilities.Load(personId, null, null, ct), baseUrl);
}
