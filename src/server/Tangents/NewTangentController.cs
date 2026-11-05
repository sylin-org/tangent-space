using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>What creating a Tangent sends, as a form or one JSON object.</summary>
public sealed record CreateTangentInput(
    [Description(Holds.TangentName)] string? Name = null,
    [Description(Holds.TangentDescription)] string? Description = null,
    [Description(Holds.TangentKind)] string? Kind = null,
    [Description(Holds.TangentAudience)] string? Audience = null);

/// <summary>Where a Tangent is created: <c>/t/new</c>, which no Tangent's slug is (<see cref="Tangent.NewSegment"/>). Its
/// <c>GET</c> is the form, one resource both ways (<see cref="NewTangentPage"/>). Its <c>POST</c> takes <c>name</c>,
/// <c>description</c>, <c>kind</c> and <c>audience</c>, as a form or a JSON object, derives the slug from the name, the
/// first numbered suffix of it where another Tangent holds it, orders the Tangent after the existing ones, and sends
/// <see cref="CreateTangent"/>: a form is sent on to the new Tangent's page, a JSON request reads the Tangent
/// (<c>201</c>); a refusal reads the form again with the reason inline, or a problem with the gate. A reader for whom the
/// place does not exist receives what an address with nothing there receives.</summary>
public sealed class NewTangentController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Route = "/t/" + Tangent.NewSegment;

    /// <summary>The answer when every slug the name yields belongs to another Tangent.</summary>
    public static readonly Reason NoSlug = Reason.Invalid("Every address this name gives belongs to another Tangent; choose another name.");

    /// <summary>The slug of a name with no letter or digit.</summary>
    public const string Unnamed = "tangent";

    /// <summary>How many times a create is sent again when another Tangent took its slug in the meantime.</summary>
    public const int Attempts = 3;

    [HttpGet(Route)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);
        return Ok(await Page(place, personId, ct));
    }

    [HttpPost(Route)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "create_tangent", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create a Tangent by naming it, for whoever holds ManageSpace. Sending it again makes a second one. Follow with start_topic.")]
    public async Task<IActionResult> Create(CreateTangentInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);

        var name = input.Name ?? "";
        var description = input.Description ?? "";
        var kind = TangentWrites.Choice(input.Kind, TangentWrites.Kinds, out var kindValid);
        if (!kindValid) return await Refused(place, personId, input, TangentWrites.KindRule, ct);
        var audience = TangentWrites.Choice(input.Audience, TangentWrites.Audiences, out var audienceValid);
        if (!audienceValid) return await Refused(place, personId, input, TangentWrites.AudienceRule, ct);
        if (place.Gate(Bit.ManageSpace) is { } gate) return await Refused(place, personId, input, gate, ct);

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (await Tangent.FreeSlug(Posts.Slug.From(name, Unnamed), ct) is not { } slug) return await Refused(place, personId, input, NoSlug, ct);
            var outcome = await new CreateTangent(slug, name, description,
                kind is { } chosenKind ? Enum.GetValues<PostKind>()[chosenKind] : PostKind.Topic, await Tangent.NextOrder(ct),
                audience is { } chosenAudience ? Enum.GetValues<Audience>()[chosenAudience] : Audience.Public, personId ?? "").Send();
            switch (outcome)
            {
                case Outcome<Tangent>.Done { Result: var tangent }:
                    return await TangentWrites.Written(HttpContext, tangent, BaseAddress(), personId, created: true,
                        PostWrites.TangentAddress(BaseAddress(), tangent.Id), ct);
                case Outcome<Tangent>.Refused { Reason.Kind: ReasonKind.Conflict }:
                    // Another Tangent took the slug between reading it free and the create: read again.
                    continue;
                case Outcome<Tangent>.Refused { Reason: { Kind: not ReasonKind.Absent } reason }:
                    return await Refused(place, personId, input, reason, ct);
                default:
                    return Answers.NotDone(HttpContext, outcome);
            }
        }

        return await Refused(place, personId, input, NoSlug, ct);
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again keeping what
    /// was sent, with the reason where it belongs.</summary>
    private async Task<IActionResult> Refused(Capabilities place, string? personId, CreateTangentInput input, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);
        var page = await Page(place, personId, ct) with
        {
            Name = input.Name ?? "",
            Description = input.Description ?? "",
            Kind = Sent(input.Kind, TangentWrites.Kinds, PostWrites.Word(PostKind.Topic)),
            Audience = Sent(input.Audience, TangentWrites.Audiences, TangentWrites.Word(Audience.Public))
        };
        page = TangentWrites.Concerning(reason) switch
        {
            TangentWrites.About.Name => page with { NameFailure = reason.Text },
            TangentWrites.About.Description => page with { DescriptionFailure = reason.Text },
            TangentWrites.About.Sent => page with { Failure = reason.Text },
            _ => page with { Gate = reason.Word, Create = null }
        };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    /// <summary>The word a field sent where it is one of <paramref name="words"/>, else <paramref name="otherwise"/>.</summary>
    private static string Sent(string? word, IReadOnlyList<string> words, string otherwise)
        => TangentWrites.Choice(word, words, out _) is { } index ? words[index] : otherwise;

    private async Task<NewTangentPage> Page(Capabilities place, string? personId, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var here = baseUrl.PathBase + Route;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        return new NewTangentPage(
            place.Gate(Bit.ManageSpace) is null ? TangentWrites.CreateForm(baseAddress + Route) : null,
            place.Can,
            place.Gate(Bit.ManageSpace)?.Word,
            "",
            "",
            PostWrites.Word(PostKind.Topic),
            TangentWrites.Word(Audience.Public),
            TangentWrites.Kinds,
            TangentWrites.Audiences,
            personId is null ? baseAddress + Participants.SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct));
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
