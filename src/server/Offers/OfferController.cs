using System.ComponentModel;
using System.Security.Claims;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Offers;

/// <summary>What answering an Offer sends, as a form or one JSON object.</summary>
public sealed record OfferInput([Description(Holds.Origin)] string? Origin = null);

/// <summary>An Offer's two answers, each a form its decider posts from the door or their notifications, carrying the
/// origin it offers in <see cref="Field"/>. <b>Use it</b> uses the address (<see cref="PlaceAddress.Use"/>)
/// and lands on first run's pointers, which say in one line where the place now answers, with the link to it, and
/// carry the address the site's pointers must now name; the person signs in again there. <b>Not this one</b> declines it
/// and lands on the door, which offers the next origin seen where there is one. To anyone but the place's operator an
/// Offer does not exist, nor does an origin that is not open to them (<see cref="AddressOffers.Open"/>), and the answer is
/// a missing resource's. A second post of the same answer changes nothing and lands where the first did. A use of an
/// origin Koan will not take says why, keeps nothing and leaves the Offer open to decline.</summary>
public sealed class OfferController(KoanWebBaseUrl baseUrl, AddressOffers offers) : ControllerBase
{
    public const string UsePath = "/offers/use";

    public const string DeclinePath = "/offers/decline";

    /// <summary>The form field that carries the origin the Offer offers.</summary>
    public const string Field = "origin";

    /// <summary>The form that uses the Offer of <paramref name="origin"/>, under <paramref name="baseAddress"/>.</summary>
    public static Form UseForm(string baseAddress, string origin) => Form.Posting(baseAddress + UsePath, Named(origin));

    /// <summary>The form that declines the Offer of <paramref name="origin"/>, under <paramref name="baseAddress"/>.</summary>
    public static Form DeclineForm(string baseAddress, string origin) => Form.Posting(baseAddress + DeclinePath, Named(origin));

    private static Form.Field Named(string origin) => new(Field, Holds.Origin, origin);

    [HttpPost(UsePath)]
    [Tool(Name = "use_offer", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Use the address an open Offer from arrive proposes, for the one who decides it: an origin the place is reached at becomes its address, and whoever is signed in signs in again there.")]
    public async Task<IActionResult> Use(OfferInput input, CancellationToken ct)
    {
        var (refusal, personId, origin, decided) = await Posted(input, ct);
        if (refusal is not null) return refusal;
        if (decided is not null) return decided.State == OfferState.Accepted ? Landed() : NoLongerOpen();

        var outcome = await PlaceAddress.Use(baseUrl, new UseOffer(origin!, personId!, origin!));
        switch (outcome)
        {
            case Outcome<Offer>.Done:
                return Landed();
            case Outcome<Offer>.Refused { Reason: var reason } when reason == Offer.NotOpen:
                return (await Offer.Get(Offer.IdFor(OfferKind.Address, origin!), ct))?.State == OfferState.Accepted ? Landed() : NoLongerOpen();
            case Outcome<Offer>.Refused { Reason: { Kind: not ReasonKind.Absent } refused }:
                return new Problem(StatusCodes.Status409Conflict, "Not used", refused.Text).ToResult();
            default:
                return PostWrites.Answer(HttpContext, outcome);
        }
    }

    [HttpPost(DeclinePath)]
    [Tool(Name = "decline_offer", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Decline the address an open Offer from arrive proposes, for the one who decides it; the place never offers it again, and arrive shows the next.")]
    public async Task<IActionResult> Decline(OfferInput input, CancellationToken ct)
    {
        var (refusal, personId, origin, decided) = await Posted(input, ct);
        if (refusal is not null) return refusal;
        if (decided is not null) return decided.State == OfferState.Declined ? Door() : NoLongerOpen();

        var outcome = await new DeclineOffer(origin!, personId!).Send();
        switch (outcome)
        {
            case Outcome<Offer>.Done:
                return Door();
            case Outcome<Offer>.Refused { Reason: var reason } when reason == Offer.NotOpen:
                return (await Offer.Get(Offer.IdFor(OfferKind.Address, origin!), ct))?.State == OfferState.Declined ? Door() : NoLongerOpen();
            case Outcome<Offer>.Refused { Reason: { Kind: not ReasonKind.Absent } refused }:
                return new Problem(StatusCodes.Status409Conflict, "Not declined", refused.Text).ToResult();
            default:
                return PostWrites.Answer(HttpContext, outcome);
        }
    }

    /// <summary>What every answer asks first: a signed-in poster who is the place's operator, and the origin its
    /// <see cref="Field"/> names, sent as a form or a JSON object, either decided before or open to them now.</summary>
    private async Task<(IActionResult? Refusal, string? PersonId, string? Origin, Offer? Decided)> Posted(OfferInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (PersonId(User) is not { } personId)
            return (PostWrites.Refused(HttpContext, Capabilities.SignIn, "Deciding an offer needs its decider signed in."), null, null, null);
        if (input.Origin is not { Length: > 0 } origin || await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space || space.OperatorId != personId)
            return (Absent(), null, null, null);
        if (await Offer.Get(Offer.IdFor(OfferKind.Address, origin), ct) is { } decided) return (null, personId, origin, decided);
        if (!string.Equals((await offers.Open(space, ct))?.Origin, origin, StringComparison.Ordinal)) return (Absent(), null, null, null);
        return (null, personId, origin, null);
    }

    /// <summary>Where Use it lands: first run's pointers, on the address the request came on.</summary>
    private IActionResult Landed()
    {
        Response.Headers.Location = baseUrl.PathBase + FirstRunController.Path;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    /// <summary>Where Not this one lands: the door, on the address the request came on.</summary>
    private IActionResult Door()
    {
        Response.Headers.Location = baseUrl.PathBase + "/";
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    private static IActionResult Absent() => PostWrites.Absent();

    private static IActionResult NoLongerOpen()
        => new Problem(StatusCodes.Status409Conflict, "No longer open", Offer.NotOpen.Text).ToResult();

    private static string? PersonId(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
}
