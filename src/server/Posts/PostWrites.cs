using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Web.Auth.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Render;

namespace Tangent.Posts;

/// <summary>What the write routes of a Post share. A write comes as a form a page posted or as JSON an agent sent, to
/// the same address, and is answered in kind: a form is sent on to the Post's address (<c>303</c>) or, refused, reads
/// its page again with the reason inline; a JSON request reads the Post with its <c>can</c>, or a problem carrying
/// the gate. A request a session or a bearer authenticated answers to <see cref="FormToken.Validate"/> before anything
/// else is read of it; one that authenticates nobody holds no credential a form could borrow, and its command refuses
/// it on its own terms.</summary>
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

    /// <summary>The refusal of a kind that is not one of <see cref="PostKind"/>'s words.</summary>
    public const string KindRule = "A Topic's kind is topic or article.";

    /// <summary>The refusal of an edit that names no version.</summary>
    public const string VersionRule = "An edit carries the version of the Post its editor read, as a whole number.";

    /// <summary>What a refusal's reason concerns: a field of what was sent, what was sent as a whole, or a gate, a verb
    /// the sender does not hold.</summary>
    public enum About
    {
        Title,
        Body,
        Sent,
        Gate
    }

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

    /// <summary>The fields a write sent, by name: a form's, or the string, number and true or false members of a JSON
    /// object, each as its text; an empty
    /// set for a body of neither kind, and null for a JSON body that is not one object.</summary>
    public static async Task<IReadOnlyDictionary<string, string>?> Fields(HttpRequest request, CancellationToken ct)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.HasFormContentType)
        {
            foreach (var (name, values) in await request.ReadFormAsync(ct))
            {
                if (values.FirstOrDefault() is { } value) fields[name] = value;
            }

            return fields;
        }

        if (!request.HasJsonContentType()) return fields;
        try
        {
            using var json = await JsonDocument.ParseAsync(request.Body, new JsonDocumentOptions { MaxDepth = 8 }, ct);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var member in json.RootElement.EnumerateObject())
            {
                if (member.Value.ValueKind == JsonValueKind.String) fields[member.Name] = member.Value.GetString()!;
                else if (member.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False) fields[member.Name] = member.Value.GetRawText();
            }

            return fields;
        }
        catch (JsonException)
        {
            return null;
        }
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
    public static async Task<(Located? Found, IActionResult? Unseen)> Locate(string? personId, string tangent, string? topic, string? post,
        CancellationToken ct)
    {
        var row = Slug.IsValid(tangent) ? await Tangents.Tangent.Get(tangent, ct) : null;
        var rows = await Capabilities.LoadRows(personId, row?.Id, ct);
        var here = rows.For(null);
        if (!here.Exists) return (null, Unseen(here.Gate(Bit.View)));
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

    /// <summary>The version an edit names, or null when the field is not a whole number.</summary>
    public static long? Version(IReadOnlyDictionary<string, string> fields)
        => long.TryParse(fields.GetValueOrDefault(VersionField), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;

    /// <summary>The kind a start names: null for none, the kind for one of its words, and false in <paramref name="valid"/>
    /// for anything else.</summary>
    public static PostKind? Kind(IReadOnlyDictionary<string, string> fields, out bool valid)
    {
        valid = true;
        var word = fields.GetValueOrDefault(KindField);
        if (string.IsNullOrEmpty(word)) return null;
        foreach (var kind in Enum.GetValues<PostKind>())
        {
            if (string.Equals(Word(kind), word, StringComparison.OrdinalIgnoreCase)) return kind;
        }

        valid = false;
        return null;
    }

    public static string Word(PostKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>The body a write route hands its command: judged (<see cref="PostBody.Of"/>) only where the person holds
    /// the verb the command needs here, so a request the writer will refuse at its gate never pays for a parse; left
    /// unjudged otherwise.</summary>
    public static PostBody Body(IReadOnlyDictionary<string, string> fields, bool mayWrite)
    {
        var text = fields.GetValueOrDefault(BodyField) ?? "";
        return mayWrite ? PostBody.Of(text) : PostBody.Unjudged(text);
    }

    public static string TangentAddress(string baseUrl, string tangent) => baseUrl + "/t/" + Uri.EscapeDataString(tangent);

    public static string TopicAddress(string baseUrl, string tangent, string topic)
        => TangentAddress(baseUrl, tangent) + "/" + Uri.EscapeDataString(topic);

    /// <summary>A Post's address, which the Topic answers opened around it: <c>/t/{tangent}/{topic}/{post}</c>.</summary>
    public static string Address(string baseUrl, string tangent, string topic, string post)
        => TopicAddress(baseUrl, tangent, topic) + "/" + Uri.EscapeDataString(post);

    /// <summary>What a refusal's reason concerns: the title's and body's rules, the rest of what was sent, or a gate.</summary>
    public static About Concerning(string reason) => reason switch
    {
        Post.NeedsTitle or Post.TitleTooLong => About.Title,
        Post.NeedsBody or Post.BodyTooLong or Post.BodyHasNul or Post.BodyRendersTooLong or Post.TooManyMentions or Post.BodyTooIntricate => About.Body,
        Post.NeedsTid or Post.NeedsTangent or Post.NeedsAuthor or Post.NotARoot or Post.TopicLocked or Post.TopicRemoved
            or Post.NoTitleOnReply or Post.BodyNotJudged or Post.PostRemoved or Post.NeedsRemover or Post.IdTaken or Reply.NoParent or KindRule or VersionRule
            => About.Sent,
        _ => About.Gate
    };

    /// <summary>A write that committed: a form is sent on to the Post's address; a JSON request reads the Post with the
    /// writer's <c>can</c> on it — a reply's judged by its Topic's <paramref name="root"/> — created (<c>201</c>) or
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
        => new Problem(StatusCodes.Status404NotFound, "Not found", "Nothing answers at this address.", Capabilities.Absent).ToResult();

    /// <summary>What a reader without <see cref="Bit.View"/> where they ask receives: the missing resource's answer where
    /// it is absent to them, otherwise the one step between them and it.</summary>
    public static IActionResult Unseen(string? gate) => gate is null or Capabilities.Absent ? Absent() : Gated(gate);

    public static IActionResult TokenRefused()
        => new Problem(StatusCodes.Status400BadRequest, "Open the form again",
            "This form’s token is missing or has expired, so nothing was written.").ToResult();

    public static IActionResult Unreadable()
        => new Problem(StatusCodes.Status400BadRequest, "Not a form or a JSON object",
            "A write sends its fields as a form, or as one JSON object.").ToResult();

    /// <summary>The problem a refusal answers as for its reason: a gate, or what was sent.</summary>
    public static IActionResult Refused(string reason) => Concerning(reason) == About.Gate
        ? Gated(reason)
        : new Problem(StatusCodes.Status400BadRequest, "Not written", reason).ToResult();

    /// <summary>The problem a gate answers as: <c>401</c> to sign in, <c>403</c> for every other gate, with the gate.</summary>
    public static IActionResult Gated(string gate)
        => new Problem(Status(gate), Heading(gate), Sentence(gate), gate).ToResult();

    public static IActionResult Conflicting(string reason)
        => new Problem(StatusCodes.Status409Conflict, "Already written", reason).ToResult();

    /// <summary>The status a refusal answers with: a gate's, or <c>400</c> for what was sent.</summary>
    public static int Status(string reason) => Concerning(reason) != About.Gate
        ? StatusCodes.Status400BadRequest
        : reason == Capabilities.SignIn ? StatusCodes.Status401Unauthorized : StatusCodes.Status403Forbidden;

    public static string Heading(string gate) => gate switch
    {
        Capabilities.SignIn => "Sign in first",
        Capabilities.Apply => "Waiting to be admitted",
        Capabilities.TimedOut => "Timed out",
        Capabilities.Locked => "Locked",
        Capabilities.Removed => "Removed",
        Capabilities.NotYours => "Not yours",
        _ => "Not open to you"
    };

    /// <summary>The one sentence a person reads for a gate, where the form is, and an agent reads as the problem's
    /// detail.</summary>
    public static string Sentence(string gate) => gate switch
    {
        Capabilities.SignIn => "Sign in to write here.",
        Capabilities.Apply => "You can write here once the proprietor admits you.",
        Capabilities.TimedOut => "You’re timed out: you can read here, and write again once it ends.",
        Capabilities.Locked => "This topic is locked, so no one replies in it.",
        Capabilities.Removed => "This post is removed, so nothing changes it now.",
        Capabilities.NotYours => "Only its author, or a moderator, can change this post.",
        _ => "This isn’t open to you here: " + gate + "."
    };

    /// <summary>Who is signed in, as the shell's account menu shows them, signing out back to <paramref name="back"/>, a
    /// path under the base URL, with the place's settings where they hold <see cref="Bit.ManageSpace"/>; null for a
    /// visitor.</summary>
    public static async Task<Who?> Reader(string? personId, Capabilities capabilities, string baseUrl, string back, CancellationToken ct)
    {
        if (personId is null) return null;
        var participant = await Participant.Get(personId, ct);
        var handle = participant?.Handle is { Length: > 0 } stored ? stored : personId;
        var name = participant?.Profile.DisplayName is { Length: > 0 } displayName ? displayName : handle;
        return new Who(personId, name, handle, Roles.Word(capabilities.RoleIds), participant?.Profile.Avatar,
            baseUrl + AuthConstants.Routes.Logout + "?return=" + Uri.EscapeDataString(back), await SettingsFor(personId, baseUrl, ct));
    }

    /// <summary>The place's settings under <paramref name="baseUrl"/> for a participant who holds
    /// <see cref="Bit.ManageSpace"/> in the place, judged in the place rather than in any one Tangent; null for anyone
    /// else.</summary>
    public static async Task<string?> SettingsFor(string personId, string baseUrl, CancellationToken ct)
        => (await Capabilities.Load(personId, null, null, ct)).Holds(Bit.ManageSpace) ? baseUrl + Space.TangentSpaceSettingsController.Path : null;
}
