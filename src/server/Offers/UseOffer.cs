using Koan.Data.Core;
using Koan.Data.Core.Commands;

namespace Tangent.Offers;

/// <summary>Uses the Offer of <see cref="Origin"/>: in one commit the Offer is accepted and <see cref="Address"/>, the
/// origin as Koan validated it, becomes the place's address. Judged here, at the moment it runs: the poster is still the
/// place's operator and the origin was never decided (<see cref="Decision"/>), the place's address is still local, and
/// the address is the origin offered. Koan's accept sends it through <see cref="Space.PlaceAddress.Use"/>, and holds the
/// origin only once it commits.</summary>
public sealed record UseOffer(string Origin, string PosterId, string Address, DateTimeOffset? At = null) : ICommand<Offer>
{
    public static readonly Reason NotOffered = Reason.Conflict("This is not the address the offer offers.");

    public async Task<Outcome<Offer>> Run(CancellationToken ct)
    {
        var space = await Decision.Open(Origin, PosterId, ct);
        if (!space.AddressIsLocal()) return new Outcome<Offer>.Refused(Offer.NotOpen);
        if (!string.Equals(Origin, Address, StringComparison.OrdinalIgnoreCase)) return new Outcome<Offer>.Refused(NotOffered);

        if (space.TakeAddress(Address)) await space.Save(ct);
        var accepted = await Offer.Decided(OfferKind.Address, Origin, OfferState.Accepted, At ?? DateTimeOffset.UtcNow).Save(ct);
        return new Outcome<Offer>.Done(accepted);
    }
}
