using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Render;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>What taking a Post on a tangent sends, as a form or one JSON object: <see cref="Tangent"/> is the Tangent the
/// new Topic starts in, so the route calls the Tangent it comes from something else.</summary>
public sealed record TakeInput(
    [Description(Holds.TakeTangent)] string? Tangent = null,
    [Description(Holds.TakeTitle)] string? Title = null,
    [Description(Holds.TakeBody)] string? Body = null,
    [Description(Holds.Id)] string? Id = null);

/// <summary>Where a Post is taken on a tangent: <c>/t/{tangent}/{topic}/{post}/tangent</c>, beside its edit, named by the
/// verb's last word. Its <c>GET</c> is the form, one resource both ways (<see cref="TakeOnATangentPage"/>), with a TID
/// minted into it. Its <c>POST</c> takes <see cref="TakeInput"/> and sends <see cref="StartTopic"/> with the Post as its
/// origin: a form is sent on to the new Topic's address,
/// a JSON request reads its root (<c>201</c>); a refusal reads the form again with the reason inline, or a problem with
/// the gate; the same id with other words is a conflict. Both resolve the address as the Topic page does
/// (<see cref="PostWrites.Locate"/>) before anything else is judged, so a Post the reader may not view and one no row
/// has answer alike. A Tangent chosen that no row has, or that is absent to the reader, is refused in one sentence
/// that says neither.</summary>
public sealed class TakeOnATangentController(KoanWebBaseUrl baseUrl, IDataProtectionProvider protection) : ControllerBase
{
    /// <summary>The segment under a Post's address where it is taken on a tangent.</summary>
    public const string Segment = "tangent";

    public const string Route = "/t/{tangent}/{topic}/{post}/" + Segment;

    /// <summary>The same address as a write takes it, whose <see cref="TakeInput.Tangent"/> names where the Topic starts.</summary>
    private const string TakenFrom = "/t/{from}/{topic}/{post}/" + Segment;

    /// <summary>The field that names the Tangent chosen, by slug.</summary>
    public const string TangentField = "tangent";

    /// <summary>The query parameter that carries the cursor of the page of Tangents to choose from.</summary>
    public const string After = "after";

    /// <summary>What a choice of a Tangent that is not one the reader may start a Topic in is told.</summary>
    public const string ChooseListed = "Choose one of the Tangents listed.";

    /// <summary>The form that takes a Post on a tangent at <paramref name="action"/>: a TID minted for the new Topic, the
    /// Tangent it starts in, <paramref name="chosen"/> as the page chose it, its title, and the taker's own words.</summary>
    public static Form TakeForm(string action, string? chosen) => Form.Posting(action,
        Form.Minted(PostWrites.IdField, Tid.Mint(DateTimeOffset.UtcNow)),
        new Form.Field(TangentField,
            Holds.TakeTangent + " One of the Tangents this form lists on any of its pages; its value is the one the page chose.",
            chosen),
        new Form.Field(PostWrites.TitleField, Holds.TakeTitle),
        new Form.Field(PostWrites.BodyField, Holds.TakeBody));

    [HttpGet(Route)]
    public async Task<IActionResult> Get([FromRoute] string tangent, [FromRoute] string topic, [FromRoute] string post,
        [FromQuery(Name = After)] string? after, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, tangent, topic, post, ct);
        if (found is null) return unseen!;
        ViewableTangents.Cursor? cursor = null;
        if (after is not null && (cursor = ViewableTangents.Cursor.Open(after, protection)) is null)
            return new Problem(StatusCodes.Status400BadRequest, "Not a page of these Tangents",
                $"The {After} parameter is the cursor a page of this form names as its next.").ToResult();
        return Ok(await Page(found, personId, null, cursor, ct));
    }

    [HttpPost(TakenFrom)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "take_on_a_tangent", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Make a Post the start of a new Topic in a Tangent you choose, linked both ways, when a reply would wander from its Topic. Send an id, a TID you mint, and the same call again returns the same Topic, so a retry is safe; without one, each call makes a new Topic. Refused, its gate says why and its next, where there is one, what to do now.")]
    public async Task<IActionResult> Take([FromRoute, Description(Holds.TakeFrom)] string from, [FromRoute, Description(Holds.Topic)] string topic,
        [FromRoute, Description(Holds.Post)] string post, TakeInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, from, topic, post, ct);
        if (found is null) return unseen!;

        var chosen = input.Tangent ?? "";
        // The chosen Tangent's row before its overwrites; one no row has is refused as one absent to the reader is.
        if ((Slug.IsValid(chosen) ? await Tangents.Tangent.Get(chosen, ct) : null) is not { } into)
            return await Refused(found, personId, input, StartTopic.NoTangent, ct);
        var mayStart = (await Capabilities.Load(personId, into.Id, null, ct)).Holds(Bit.Topic);
        var title = input.Title ?? "";
        var body = await PostWrites.Body(input.Body, mayStart);
        var outcome = await new StartTopic(
            PostWrites.Id(input.Id),
            into.Id,
            title,
            body,
            personId ?? "",
            OriginId: found.Post!.Id).Send();
        switch (outcome)
        {
            case Outcome<Post>.Done { Result: var root }:
                return await PostWrites.Written(HttpContext, root, root, PostWrites.Address(BaseAddress(), root.TangentId, root.Slug ?? root.Id, root.Id),
                    personId, created: true, ct);
            case Outcome<Post>.Refused { Reason: var reason } when reason != StartTopic.NoOrigin && reason.Kind != ReasonKind.Conflict:
                return await Refused(found, personId, input, reason, ct);
            case Outcome<Post>.Refused { Reason: { Kind: ReasonKind.Conflict } reason }:
                if (PostWrites.WantsJson(Request)) return PostWrites.Refused(HttpContext, reason);
                return await Again(found, personId, input, StatusCodes.Status409Conflict, keepId: false, page => page with
                {
                    Failure = "This form was already sent with other words, so nothing was posted. Send it again to post these words as well."
                }, ct);
            default:
                return PostWrites.Answer(HttpContext, outcome);
        }
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as — a Tangent chosen that the reader may
    /// not start a Topic in, said without saying whether it exists; for a form, the form again with the reason where it
    /// belongs.</summary>
    private async Task<IActionResult> Refused(PostWrites.Located found, string? personId, TakeInput input, Reason reason, CancellationToken ct)
    {
        if (reason == StartTopic.NoTangent)
        {
            if (PostWrites.WantsJson(Request)) return new Problem(StatusCodes.Status400BadRequest, "Not written", reason.Text).ToResult();
            return await Again(found, personId, input, StatusCodes.Status400BadRequest, keepId: true, page => page with { ChoiceFailure = ChooseListed }, ct);
        }

        if (PostWrites.WantsJson(Request)) return PostWrites.Refused(HttpContext, reason);
        return await Again(found, personId, input, PostWrites.Status(reason), keepId: true, page => PostWrites.Concerning(reason) switch
        {
            PostWrites.About.Title => page with { TitleFailure = reason.Text },
            PostWrites.About.Body => page with { BodyFailure = reason.Text },
            PostWrites.About.Sent => page with { Failure = reason.Text },
            _ => page.Gate is null ? page with { ChoiceFailure = reason.Text } : page
        }, ct);
    }

    /// <summary>The form again, read afresh, keeping the choice and the words that were sent, and the Id when it is a TID
    /// and <paramref name="keepId"/>; otherwise a new one is minted, so sending again starts a Topic of its own.</summary>
    private async Task<IActionResult> Again(PostWrites.Located sent, string? personId, TakeInput input, int status,
        bool keepId, Func<TakeOnATangentPage, TakeOnATangentPage> refusal, CancellationToken ct)
    {
        var (found, unseen) = await PostWrites.Locate(HttpContext, personId, sent.Tangent.Id, sent.Root!.Slug ?? sent.Root.Id, sent.Post!.Id, ct);
        if (found is null) return unseen!;
        var page = await Page(found, personId, input.Tangent, null, ct) with { Title = input.Title, Body = input.Body };
        if (keepId && Tid.IsValid(input.Id)) page = page with { Take = page.Take?.With(PostWrites.IdField, input.Id) };
        return new ObjectResult(refusal(page)) { StatusCode = status };
    }

    /// <summary>The form for the Post the address names: a page of the Tangents the reader may start a Topic in, after
    /// <paramref name="after"/> or from the first (<see cref="Origins.Destinations"/>), with the origin's own where they
    /// may start one there and <paramref name="chosen"/> where they may start one there, and the next page and the first a
    /// link away; one chosen: <paramref name="chosen"/> where it is among them, else the origin's Tangent, else the first.
    /// The gate is <see cref="Capabilities.Removed"/> for a tombstone, and where no Tangent is listed, the origin's
    /// Tangent's reason the reader may not start a Topic there.</summary>
    private async Task<TakeOnATangentPage> Page(PostWrites.Located found, string? personId, string? chosen, ViewableTangents.Cursor? after,
        CancellationToken ct)
    {
        var origin = found.Post!;
        var root = found.Root!;
        var here = found.Rows.For(null);
        var baseAddress = BaseAddress();
        var tangentAddress = PostWrites.TangentAddress(baseAddress, found.Tangent.Id);
        var topicAddress = PostWrites.TopicAddress(baseAddress, found.Tangent.Id, root.Slug ?? root.Id);
        var address = topicAddress + "/" + Uri.EscapeDataString(origin.Id);
        var back = baseUrl.PathBase + "/t/" + found.Tangent.Id + "/" + (root.Slug ?? root.Id) + "/" + origin.Id + "/" + Segment;

        var listed = await Origins.Destinations(personId, after, Origins.DestinationLimit, ct);
        var destinations = listed.Tangents.ToList();
        if (here.Holds(Bit.Topic) && destinations.All(row => row.Id != found.Tangent.Id)) destinations.Add(found.Tangent);
        if (personId is not null && chosen is not null && Slug.IsValid(chosen) && destinations.All(row => row.Id != chosen)
            && await Tangents.Tangent.Get(chosen, ct) is { } kept && (await Capabilities.Load(personId, kept.Id, null, ct)).Holds(Bit.Topic))
            destinations.Add(kept);
        var choices = destinations.Select(row => new TakeOnATangentPage.Choice(row.Id, row.Name, PostWrites.Word(row.DefaultKind))).ToList();
        var choice = choices.FirstOrDefault(listed => listed.Slug == chosen)
                     ?? choices.FirstOrDefault(listed => listed.Slug == found.Tangent.Id)
                     ?? choices.FirstOrDefault();
        var gate = origin.Removed ? Capabilities.Removed.Word
            : choices.Count == 0 ? (here.Gate(Bit.Topic) ?? Capabilities.NoRoleGrants(Bit.Topic)).Word
            : null;

        var people = await People(origin, ct);
        var author = people.TryGetValue(origin.AuthorId, out var person) ? person : new TangentPage.Author(origin.AuthorId, origin.AuthorId, origin.AuthorId, "person", null);
        return new TakeOnATangentPage(
            new TakeOnATangentPage.Quoted(origin.Id, address, author, origin.CreatedAt, origin.Removed ? null : TangentPage.Opening(origin, people),
                origin.Removed, root.Title ?? "", topicAddress, found.Tangent.Name, tangentAddress, TangentPage.EditedOf(origin, people)),
            choices,
            choice?.Slug,
            gate is null ? TakeForm(address + "/" + Segment, choice?.Slug) : null,
            found.Capabilities.Can,
            gate,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(back) : null,
            found.Rows.Space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, here, baseAddress, back, ct))
        {
            More = listed.Next is { } next ? address + "/" + Segment + "?" + After + "=" + Uri.EscapeDataString(next.Seal(protection)) : null,
            First = after is null ? null : address + "/" + Segment
        };
    }

    /// <summary>The origin's author and the participants its body mentions, in one keyed read, by participant Id.</summary>
    internal static async Task<IReadOnlyDictionary<string, TangentPage.Author>> People(Post origin, CancellationToken ct)
    {
        var ids = origin.MentionedIds.Prepend(origin.AuthorId).Concat(origin.EditedBy is { } editor ? [editor] : []).Distinct(StringComparer.Ordinal).ToList();
        return (await Participant.Get(ids, ct)).OfType<Participant>().ToDictionary(row => row.Id, row =>
        {
            var handle = row.Handle.Length > 0 ? row.Handle : row.Id;
            return new TangentPage.Author(row.Id, handle, row.Profile.DisplayName is { Length: > 0 } name ? name : handle,
                row.Kind.ToString().ToLowerInvariant(), row.Profile.Avatar);
        }, StringComparer.Ordinal);
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
