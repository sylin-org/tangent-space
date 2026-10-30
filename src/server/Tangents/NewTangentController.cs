using Koan.Data.Abstractions;
using Koan.Data.Core.Sorting;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Tangents;

/// <summary>Where a Tangent is created: <c>/t/new</c>, which no Tangent's slug is (<see cref="Tangent.NewSegment"/>). Its
/// <c>GET</c> is the form, one resource both ways (<see cref="NewTangentPage"/>). Its <c>POST</c> takes <c>name</c>,
/// <c>description</c>, <c>kind</c> and <c>audience</c>, as a form or a JSON object, derives the slug from the name, the
/// first numbered suffix of it where another Tangent holds it, orders the Tangent after the existing ones, and sends
/// <see cref="CreateTangent"/>: a form is sent on to the new Tangent's page, a JSON request reads the Tangent
/// (<c>201</c>); a refusal reads the form again with the reason inline, or a problem with the gate. A reader for whom the
/// place does not exist receives what an address with nothing there receives.</summary>
public sealed class NewTangentController(KoanWebBaseUrl baseUrl, WriteLoop writer) : ControllerBase
{
    public const string Route = "/t/" + Tangent.NewSegment;

    /// <summary>The answer when every slug the name yields belongs to another Tangent.</summary>
    public const string NoSlug = "Every address this name gives belongs to another Tangent; choose another name.";

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
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        return Ok(await Page(place, personId, ct));
    }

    [HttpPost(Route)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();

        var name = fields.GetValueOrDefault(TangentWrites.NameField) ?? "";
        var description = fields.GetValueOrDefault(TangentWrites.DescriptionField) ?? "";
        var kind = TangentWrites.Choice(fields, TangentWrites.KindField, TangentWrites.Kinds, out var kindValid);
        if (!kindValid) return await Refused(place, personId, fields, TangentWrites.KindRule, ct);
        var audience = TangentWrites.Choice(fields, TangentWrites.AudienceField, TangentWrites.Audiences, out var audienceValid);
        if (!audienceValid) return await Refused(place, personId, fields, TangentWrites.AudienceRule, ct);
        if (place.Gate(Bit.ManageSpace) is { } gate) return await Refused(place, personId, fields, gate, ct);

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (await Tangent.FreeSlug(Posts.Slug.From(name, Unnamed), ct) is not { } slug) return await Refused(place, personId, fields, NoSlug, ct);
            var outcome = await writer.Send(new CreateTangent(slug, name, description,
                kind is { } chosenKind ? Enum.GetValues<PostKind>()[chosenKind] : PostKind.Topic, await NextOrder(ct),
                audience is { } chosenAudience ? Enum.GetValues<Audience>()[chosenAudience] : Audience.Public, personId ?? ""), ct);
            switch (outcome)
            {
                case Outcome<Tangent>.Done { Result: var tangent }:
                    return await TangentWrites.Written(HttpContext, tangent, BaseAddress(), personId, created: true,
                        PostWrites.TangentAddress(BaseAddress(), tangent.Id), ct);
                case Outcome<Tangent>.Refused { Reason: CreateTangent.NoPlace }:
                    return PostWrites.Absent();
                case Outcome<Tangent>.Refused { Reason: var reason }:
                    return await Refused(place, personId, fields, reason, ct);
                case Outcome<Tangent>.Conflict:
                    // Another Tangent took the slug between reading it free and the create: read again.
                    continue;
                case Outcome<Tangent>.Failed { Cause: var cause }:
                    throw new InvalidOperationException("Creating a Tangent failed.", cause);
                default:
                    throw new InvalidOperationException($"The writer answered {outcome} for creating a Tangent.");
            }
        }

        return await Refused(place, personId, fields, NoSlug, ct);
    }

    /// <summary>The order that puts a new Tangent after the existing ones, the moderators' Tangent aside, which comes after
    /// every other: one more than the highest below it; one bounded read of one row.</summary>
    private static async Task<int> NextOrder(CancellationToken ct)
    {
        var last = await Tangent.Query(tangent => tangent.Order < ModeratorsTangent.Order,
            new QueryDefinition { Page = 1, PageSize = 1 }.WithSort<Tangent>(sort => sort.OrderByDescending(tangent => tangent.Order)), ct);
        return last.Count == 0 ? 1 : last[0].Order + 1;
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the form again keeping what
    /// was sent, with the reason where it belongs.</summary>
    private async Task<IActionResult> Refused(Capabilities place, string? personId, IReadOnlyDictionary<string, string> fields, string reason,
        CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return TangentWrites.Refused(reason);
        var page = await Page(place, personId, ct) with
        {
            Name = fields.GetValueOrDefault(TangentWrites.NameField) ?? "",
            Description = fields.GetValueOrDefault(TangentWrites.DescriptionField) ?? "",
            Kind = Sent(fields, TangentWrites.KindField, TangentWrites.Kinds, PostWrites.Word(PostKind.Topic)),
            Audience = Sent(fields, TangentWrites.AudienceField, TangentWrites.Audiences, TangentWrites.Word(Audience.Public))
        };
        page = TangentWrites.Concerning(reason) switch
        {
            TangentWrites.About.Name => page with { NameFailure = reason },
            TangentWrites.About.Description => page with { DescriptionFailure = reason },
            TangentWrites.About.Sent => page with { Failure = reason },
            _ => page with { Gate = reason }
        };
        return new ObjectResult(page) { StatusCode = TangentWrites.Status(reason) };
    }

    /// <summary>The word a field sent where it is one of <paramref name="words"/>, else <paramref name="otherwise"/>.</summary>
    private static string Sent(IReadOnlyDictionary<string, string> fields, string field, IReadOnlyList<string> words, string otherwise)
        => TangentWrites.Choice(fields, field, words, out _) is { } index ? words[index] : otherwise;

    private async Task<NewTangentPage> Page(Capabilities place, string? personId, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var here = baseUrl.PathBase + Route;
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        return new NewTangentPage(
            baseAddress + Route,
            FormToken.For(HttpContext),
            place.Can,
            place.Gate(Bit.ManageSpace),
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
