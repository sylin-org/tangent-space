using System.Security.Claims;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Render;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Offers;

/// <summary>An Offer's two answers, each a form its decider posts from the door, carrying the Offer in
/// <see cref="Field"/>, under <see cref="FormToken.Validate"/>. <b>Use it</b> uses the address (<see cref="PlaceAddress.Use"/>)
/// and lands on first run's pointers, which say in one line where the place now answers, with the link to it, and
/// carry the address the site's pointers must now name; the person signs in again there. <b>Not this one</b> declines it
/// and lands on the door, offering the next origin seen where there is one. To anyone but the place's operator an
/// Offer does not exist, and the answer is a missing resource's. A second post of the same answer changes nothing
/// and lands where the first did, and where the address the first one kept is not the one Koan holds — its request
/// ended after the writer kept it — Koan is made to hold it.</summary>
public sealed class OfferController(KoanWebBaseUrl baseUrl, WriteLoop writer, AddressOffers offers) : ControllerBase
{
    public const string UsePath = "/offers/use";

    public const string DeclinePath = "/offers/decline";

    /// <summary>The form field that carries the Offer's Id.</summary>
    public const string Field = "offer";

    [HttpPost(UsePath)]
    public async Task<IActionResult> Use([FromForm(Name = Field)] string? offer, CancellationToken ct)
    {
        var (refusal, personId, row) = await Posted(offer, ct);
        if (refusal is not null) return refusal;
        if (row!.State == OfferState.Accepted) return await Landed(row, ct);
        if (row.State != OfferState.Open || baseUrl.IsConfigured) return NoLongerOpen();

        switch (await PlaceAddress.Use(baseUrl, new UseOffer(row.Id, personId!, row.Subject), ct))
        {
            case Outcome<Offer>.Done:
                return await Landed(row, ct);
            case Outcome<Offer>.Refused { Reason: Decision.NoOffer }:
                return Absent();
            case Outcome<Offer>.Refused { Reason: Offer.NotOpen }:
                return (await Offer.Get(row.Id, ct))?.State == OfferState.Accepted ? await Landed(row, ct) : NoLongerOpen();
            case Outcome<Offer>.Refused refused:
                return new Problem(StatusCodes.Status409Conflict, "Not used", refused.Reason).ToResult();
            case Outcome<Offer>.Failed failed:
                throw new InvalidOperationException($"Using the offer {row.Id} failed.", failed.Cause);
            case var other:
                throw new InvalidOperationException($"The writer answered {other} for using the offer {row.Id}.");
        }
    }

    [HttpPost(DeclinePath)]
    public async Task<IActionResult> Decline([FromForm(Name = Field)] string? offer, CancellationToken ct)
    {
        var (refusal, personId, row) = await Posted(offer, ct);
        if (refusal is not null) return refusal;
        if (row!.State == OfferState.Declined) return Door();
        if (row.State != OfferState.Open) return NoLongerOpen();

        switch (await writer.Send(new DeclineOffer(row.Id, personId!), ct))
        {
            case Outcome<Offer>.Done:
                await offers.OfferNext(ct);
                return Door();
            case Outcome<Offer>.Refused { Reason: Decision.NoOffer }:
                return Absent();
            case Outcome<Offer>.Refused { Reason: Offer.NotOpen }:
                return (await Offer.Get(row.Id, ct))?.State == OfferState.Declined ? Door() : NoLongerOpen();
            case Outcome<Offer>.Refused refused:
                return new Problem(StatusCodes.Status409Conflict, "Not declined", refused.Reason).ToResult();
            case Outcome<Offer>.Failed failed:
                throw new InvalidOperationException($"Declining the offer {row.Id} failed.", failed.Cause);
            case var other:
                throw new InvalidOperationException($"The writer answered {other} for declining the offer {row.Id}.");
        }
    }

    /// <summary>What every answer asks first: a signed-in poster, the form's token, and an Offer that exists for them,
    /// which only the place's operator has.</summary>
    private async Task<(IActionResult? Refusal, string? PersonId, Offer? Row)> Posted(string? offer, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (PersonId(User) is not { } personId)
            return (new Problem(StatusCodes.Status401Unauthorized, "Sign in first", "Deciding an offer needs its decider signed in.",
                Capabilities.SignIn).ToResult(), null, null);
        if (!await FormToken.Validate(HttpContext))
            return (new Problem(StatusCodes.Status400BadRequest, "Open the door again",
                "This form’s token is missing or has expired, so nothing was decided.").ToResult(), null, null);
        if (offer is not { Length: > 0 } || await Offer.Get(offer, ct) is not { } row
            || (await TangentSpace.Get(TangentSpace.SingleRow, ct))?.OperatorId != personId)
            return (Absent(), null, null);
        return (null, personId, row);
    }

    /// <summary>Where Use it lands: first run's pointers, on the address the request came on, once Koan holds the address
    /// the row keeps from <paramref name="offer"/>.</summary>
    private async Task<IActionResult> Landed(Offer offer, CancellationToken ct)
    {
        if (!baseUrl.IsConfigured && (await TangentSpace.Get(TangentSpace.SingleRow, ct))?.Address is { } kept
            && kept == offer.Subject && baseUrl.Accepted != kept)
            await baseUrl.Accept(kept, ct);
        Response.Headers.Location = baseUrl.PathBase + FirstRunController.Path;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    /// <summary>Where Not this one lands: the door, on the address the request came on.</summary>
    private IActionResult Door()
    {
        Response.Headers.Location = baseUrl.PathBase + "/";
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    private static IActionResult Absent() => DoorController.Gated(Capabilities.Absent);

    private static IActionResult NoLongerOpen()
        => new Problem(StatusCodes.Status409Conflict, "No longer open", Offer.NotOpen).ToResult();

    private static string? PersonId(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
}
