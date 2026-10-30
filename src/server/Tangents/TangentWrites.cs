using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>What the routes that create a Tangent and change its settings share: their fields and the words each field
/// takes, who may manage a Tangent, what a refusal concerns, and the answer a write that committed gives. A write comes
/// as a form or as JSON and is answered in kind, as a Post's write is (<see cref="PostWrites"/>).</summary>
public static class TangentWrites
{
    public const string NameField = "name";
    public const string DescriptionField = "description";
    public const string KindField = PostWrites.KindField;
    public const string AudienceField = "audience";
    public const string MoveField = "move";

    /// <summary>The settings page's segment under a Tangent's address; <see cref="Slug.Reserved"/> keeps it from every
    /// Topic.</summary>
    public const string SettingsSegment = "settings";

    public const string KindRule = "A Tangent’s default kind is topic or article.";
    public const string AudienceRule = "A Tangent’s audience is public or private.";
    public const string MoveRule = "A Tangent moves earlier or later.";

    /// <summary>The default kinds a Tangent may take, as words.</summary>
    public static readonly IReadOnlyList<string> Kinds = Enum.GetValues<PostKind>().Select(PostWrites.Word).ToList();

    /// <summary>The audiences a Tangent may have, as words.</summary>
    public static readonly IReadOnlyList<string> Audiences = Enum.GetValues<Audience>().Select(Word).ToList();

    /// <summary>The Tangents' moves, as words.</summary>
    public static readonly IReadOnlyList<string> Moves = Enum.GetValues<Move>().Select(Word).ToList();

    /// <summary>What a refusal's reason concerns: the name's rules, the description's, the rest of what was sent, or a
    /// gate.</summary>
    public enum About
    {
        Name,
        Description,
        Sent,
        Gate
    }

    /// <summary>The form that creates a Tangent at <paramref name="action"/>: its name, and what else stands at its default
    /// until it is sent.</summary>
    public static Form CreateForm(string action, FormToken token) => Form.Posting(action, token,
        new Form.Field(NameField, $"The Tangent's name, 1 to {Tangent.NameLimit} characters; its address derives from it and never changes."),
        new Form.Field(DescriptionField, $"What it is for, at most {Tangent.DescriptionLimit} characters; left out, none."),
        new Form.Field(KindField, "The kind a Topic started there takes: topic or article; left out, topic."),
        new Form.Field(AudienceField, "Who reads it: public, everyone who reads the place, where members post, or private; left out, public."));

    /// <summary>The form that changes a Tangent's settings at <paramref name="action"/>, each field left as it is when not
    /// sent: its audience only where it is not <paramref name="fixedAudience"/>, its place among the Tangents only where
    /// the reader <paramref name="movable"/> it.</summary>
    public static Form ChangeForm(string action, bool fixedAudience, bool movable, FormToken token)
    {
        List<Form.Field> fields =
        [
            new(NameField, $"The Tangent's name, 1 to {Tangent.NameLimit} characters; left out, it stays. Its address never changes."),
            new(DescriptionField, $"What it is for, at most {Tangent.DescriptionLimit} characters; left out, it stays."),
            new(KindField, "The kind a Topic started there takes: topic or article; left out, it stays.")
        ];
        if (!fixedAudience) fields.Add(new(AudienceField, "Who reads it: public or private; left out, it stays."));
        if (movable && !fixedAudience) fields.Add(new(MoveField, "earlier or later, to move it one place among the Tangents; sent alone, it changes nothing else."));
        return new Form(action, HttpMethods.Post, fields, token);
    }

    public static string Word(Audience audience) => audience.ToString().ToLowerInvariant();

    public static string Word(Move move) => move.ToString().ToLowerInvariant();

    /// <summary>Null where the reader may change a Tangent's settings — they hold <see cref="Bit.ManageTangent"/> on it,
    /// or <see cref="Bit.ManageSpace"/> — otherwise the gate of <see cref="Bit.ManageTangent"/>.</summary>
    public static string? ManageGate(Capabilities capabilities)
        => capabilities.Holds(Bit.ManageTangent) || capabilities.Holds(Bit.ManageSpace) ? null : capabilities.Gate(Bit.ManageTangent);

    public static bool MayManage(Capabilities capabilities) => ManageGate(capabilities) is null;

    /// <summary>The word a field names among <paramref name="words"/>, by its place there; null for none, and false in
    /// <paramref name="valid"/> for a word that is not one of them.</summary>
    public static int? Choice(IReadOnlyDictionary<string, string> fields, string field, IReadOnlyList<string> words, out bool valid)
    {
        valid = true;
        var word = fields.GetValueOrDefault(field);
        if (string.IsNullOrEmpty(word)) return null;
        for (var index = 0; index < words.Count; index++)
        {
            if (string.Equals(words[index], word, StringComparison.OrdinalIgnoreCase)) return index;
        }

        valid = false;
        return null;
    }

    public static About Concerning(string reason) => reason switch
    {
        Tangent.NeedsName or Tangent.NameTooLong => About.Name,
        Tangent.DescriptionTooLong => About.Description,
        Tangent.NeedsSlug or Tangent.SlugReserved or KindRule or AudienceRule or MoveRule or ChangeTangent.Fixed or ChangeTangent.NoAudience
            or CreateTangent.NoAudience or NewTangentController.NoSlug => About.Sent,
        _ => About.Gate
    };

    /// <summary>The problem a refusal answers a JSON request with: a gate's, or <c>400</c> for what was sent.</summary>
    public static IActionResult Refused(string reason) => Concerning(reason) == About.Gate
        ? PostWrites.Gated(reason)
        : new Problem(StatusCodes.Status400BadRequest, "Not written", reason).ToResult();

    /// <summary>The status a refused form answers with: a gate's, or <c>400</c> for what was sent.</summary>
    public static int Status(string reason) => Concerning(reason) == About.Gate ? PostWrites.Status(reason) : StatusCodes.Status400BadRequest;

    /// <summary>A write that committed: a form is sent on to <paramref name="next"/>; a JSON request reads the Tangent as
    /// it now stands with the writer's <c>can</c> on it, created (<c>201</c>) or changed (<c>200</c>), with its address
    /// as its <c>Location</c>.</summary>
    public static async Task<IActionResult> Written(HttpContext http, Tangent tangent, string baseAddress, string? personId, bool created,
        string next, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var address = PostWrites.TangentAddress(baseAddress, tangent.Id);
        if (!PostWrites.WantsJson(http.Request))
        {
            http.Response.Headers.Location = next;
            return new StatusCodeResult(StatusCodes.Status303SeeOther);
        }

        http.Response.Headers.Location = address;
        var capabilities = await Capabilities.Load(personId, tangent.Id, null, ct);
        var arranges = (await Capabilities.Load(personId, null, null, ct)).Holds(Bit.ManageSpace);
        var audience = await Overwrites.AudienceOf(tangent.Id, ct);
        return new ObjectResult(new WrittenTangent(tangent.Id, tangent.Name, tangent.Description, PostWrites.Word(tangent.DefaultKind),
            Word(audience ?? Audience.Public), arranges ? tangent.Order : null, address, address + "/" + SettingsSegment, capabilities.Can))
        {
            StatusCode = created ? StatusCodes.Status201Created : StatusCodes.Status200OK,
            ContentTypes = { "application/json" }
        };
    }
}

/// <summary>A Tangent as a write leaves it, for an agent: its slug, name, description, default kind and audience, its
/// <see cref="Order"/> for a writer who holds <c>ManageSpace</c> and so arranges the place's list, its page, its settings,
/// and what the writer <see cref="Can"/> do there.</summary>
public sealed record WrittenTangent(
    string Slug,
    string Name,
    string Description,
    string Kind,
    string Audience,
    int? Order,
    string Address,
    string Settings,
    IReadOnlyList<string> Can);
