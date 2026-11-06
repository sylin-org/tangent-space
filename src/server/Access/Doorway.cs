using System.Reflection;
using System.Security.Claims;
using Koan.Data.Core.Commands;
using Koan.Web.Authorization;
using Koan.Web.Hooks;
using Koan.Web.Hosting;
using Tangent.Posts;
using Tangent.Space;

namespace Tangent.Access;

/// <summary>What a route needs of its reader: one bit, on what its address names. A route that declares it is judged by
/// <see cref="Doorway.Judge"/> once, before anything is bound, and its action reads what the judgement found (<see cref="Here.Of"/>).
/// <see cref="Participant"/> asks a signed-in participant besides, as a report does; <see cref="Outside"/> lets in someone outside a
/// members-only Tangent they may join or ask to join, as the Tangent's own address does.</summary>
public sealed class NeedsAttribute(Bit bit) : AccessAttribute<Bit>(bit)
{
    /// <summary>Whether only a signed-in participant may: a visitor is told to sign in whatever the bit says.</summary>
    public bool Participant { get; init; }

    /// <summary>Whether a participant who does not read the Tangent the address names, and may join or ask to join it
    /// (<see cref="Tangents.Inside.Outside"/>), reaches the action, which answers them with its card (<see cref="Here.Outside"/>);
    /// every other bit is still judged, and absent to them.</summary>
    public bool Outside { get; init; }
}

/// <summary>An address as it stands for its reader: its Tangent, the rows what the reader holds there is computed from, and the Topic's root
/// and the Post the address names, where it names them. <see cref="Doorway.Judge"/> keeps it on the request for the action, the frame and
/// the page.</summary>
public sealed record Here(Tangents.Tangent? Tangent, Capabilities.Rows Rows, Post? Root, Post? Post)
{
    /// <summary>Where the reader stands outside the Tangent, at a route that answers them (<see cref="NeedsAttribute.Outside"/>);
    /// null for everyone who reads it, and at every other route.</summary>
    public Tangents.Outsider? Outside { get; init; }

    /// <summary>What the reader holds on what the address names: the Post, a reply judged by its Topic's root; the Topic, by its root;
    /// otherwise the Tangent, or the place.</summary>
    public Capabilities Capabilities => Rows.For(Post ?? Root, Post is { ParentId: not null } ? Root : null);

    /// <summary>What the reader holds on the Tangent, or on the place where the address names none.</summary>
    public Capabilities Around => Rows.For(null);

    /// <summary>The reader's id; null for a visitor.</summary>
    public string? PersonId => Rows.Participant?.Id;

    /// <summary>What the judgement of this request found.</summary>
    public static Here Of(HttpContext http)
        => http.Features.Get<Here>() ?? throw new InvalidOperationException("This action declares no bit, so nothing judged its address.");

    /// <summary>An address of <c>{tangent}/{topic}/{post}</c> as it stands for <paramref name="personId"/>, null for a visitor, in the
    /// Topic page's order: the Tangent's row first, so an address no Tangent has reads no overwrites; capability's rows, once; whether the
    /// reader may view the place or the Tangent, the same for every address under it, so a visitor of a private place reads one sign-in
    /// whether an address holds something or nothing; then the missing resource's answer for a Tangent no row has, a Topic segment that
    /// names no Topic of it by its slug or its root's TID, or an Id no Post of that Topic has. The reads are the Topic page's: the Tangent,
    /// capability's rows, the root by slug and the Post by Id.</summary>
    public static async Task<(Here Here, Reason? Refusal)> Find(string? personId, string? tangent, string? topic, string? post, CancellationToken ct)
    {
        var row = tangent is not null && Slug.IsValid(tangent) ? await Tangents.Tangent.Get(tangent, ct) : null;
        var rows = await Capabilities.LoadRows(personId, row?.Id, ct);
        var nothing = new Here(row, rows, null, null);
        var around = rows.For(null);
        if (!around.Exists) return (nothing, around.Gate(Bit.View));
        if (tangent is not null && row is null) return (nothing, Capabilities.Absent);
        Post? root = null;
        if (topic is not null && (row is null || (root = await Topic(row.Id, topic, ct)) is null)) return (nothing, Capabilities.Absent);
        Post? named = null;
        if (post is not null)
        {
            named = root is null || !Tid.IsValid(post) ? null : post == root.Id ? root : await Post.Get(post, ct);
            if (named is null || named.TopicId != root!.Id) return (nothing, Capabilities.Absent);
        }

        return (new Here(row, rows, root, named), null);
    }

    /// <summary>The Topic a segment names in a Tangent: by its slug, or, where none has it, by its root's TID, so a sender who minted
    /// the TID finds the Topic it started and a reader of the log, which names a Topic by its id, finds the Topic and each of its Posts.
    /// A slug is read first, so a slug that looks like a TID names its own Topic.</summary>
    private static async Task<Post?> Topic(string tangentId, string topic, CancellationToken ct)
    {
        if (!Slug.IsValid(topic)) return null;
        if (await TopicWindow.Topic(tangentId, topic, ct) is { } root) return root;
        return Tid.IsValid(topic) && await Post.Get(topic, ct) is { ParentId: null } started && started.TangentId == tangentId ? started : null;
    }
}

/// <summary>The Doorway: Tangent's one judgement of who may reach an address, on Koan's gate before binding (ARCH-0146). For a route that
/// declares what it needs (<see cref="NeedsAttribute"/>), <see cref="Judge"/> finds the address as it stands for its reader
/// (<see cref="Here.Find"/>), refuses with the one reason <see cref="Capabilities.For"/> names, and otherwise hands the action what it
/// found. It defers on a route that declares nothing, on a listing, which asks with no address, before first run, which
/// <see cref="FirstRunGate"/> answers, and to a visitor of a private place, whom <see cref="PrivatePlaceGate"/> answers the
/// sign-in; both answer before anything is bound. Commands judge again inside their hold.</summary>
public sealed class Doorway : IAuthorizationProvider
{
    public const string TangentValue = "tangent";
    public const string TopicValue = "topic";
    public const string PostValue = "post";

    public int Order => 10;

    public async Task<AuthorizeDecision?> EvaluateAsync(AuthorizeRequest request, CancellationToken ct = default)
    {
        if (request.Resource is not MethodInfo verb || request.Context is not { } route) return null;
        var needs = Declared(verb);
        if (needs.Count == 0 || await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return null;
        var personId = PersonOf(request.Subject);
        if (personId is null && space.Audience == Audience.Private) return null;
        var (here, refusal) = await Judge(personId, needs, Value(route, TangentValue), Value(route, TopicValue), Value(route, PostValue), ct);
        return refusal is not null ? AuthorizeDecision.Refused(refusal) : AuthorizeDecision.Allowed(here);
    }

    /// <summary>The address as it stands for <paramref name="personId"/> (<see cref="Here.Find"/>), and then each bit
    /// <paramref name="needs"/> names, on what the address names; <see cref="Bit.View"/> is the address's own, which finding it asked.
    /// A Tangent absent to a participant outside it who may join or ask to join is found where <paramref name="needs"/> lets them in
    /// (<see cref="NeedsAttribute.Outside"/>).</summary>
    public static async Task<(Here Here, Reason? Refusal)> Judge(string? personId, IReadOnlyList<NeedsAttribute> needs, string? tangent, string? topic,
        string? post, CancellationToken ct)
    {
        var (here, refusal) = await Here.Find(personId, tangent, topic, post, ct);
        if (refusal == Capabilities.Absent && personId is not null && topic is null && here.Tangent is { } row && needs.Any(need => need.Outside)
            && await Tangents.Inside.Outside(row, personId, ct) is { } outside)
            (here, refusal) = (here with { Outside = outside }, null);
        if (refusal is not null) return (here, refusal);
        if (personId is null && needs.Any(need => need.Participant)) return (here, Capabilities.SignIn);
        var on = here.Capabilities;
        return (here, needs.Where(need => need.Need != Bit.View).Select(need => on.Gate(need.Need)).FirstOrDefault(gate => gate is not null));
    }

    /// <summary>What a verb declares, reached through its class: the class's, then its own, every one holding.</summary>
    public static IReadOnlyList<NeedsAttribute> Declared(MethodInfo verb)
        => (verb.ReflectedType is { } owner ? new MemberInfo[] { owner, verb } : [verb])
            .SelectMany(member => member.GetCustomAttributes<NeedsAttribute>(inherit: true)).ToList();

    private static string? PersonOf(ClaimsPrincipal subject)
        => subject.Identity?.IsAuthenticated == true ? subject.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;

    private static string? Value(IReadOnlyDictionary<string, object?> route, string name) => route.GetValueOrDefault(name) as string;
}

/// <summary>What a refusal asks of someone not signed in, beside its sentence and its word: Tangent's own scheme, naming the proof door's
/// statement, where an agent with an atproto identity of its own learns how to sign in with no browser. No client is sent into a flow that
/// does not exist here.</summary>
public sealed class ProofChallenge(KoanWebBaseUrl baseUrl) : IChallenge
{
    /// <summary>The scheme of the challenge.</summary>
    public const string Scheme = "Tangent";

    public string For(HttpContext http) => $"{Scheme} proof=\"{baseUrl.Resolve(http)}{ProofDoor.StatementPath}\"";
}
