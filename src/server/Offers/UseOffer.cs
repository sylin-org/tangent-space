using Koan.Data.Core;
using Tangent.Writer;

namespace Tangent.Offers;

/// <summary>Uses an Offer of an address: in one transaction the Offer is accepted and <see cref="Address"/>, the origin
/// it offers as Koan validated it, becomes the place's address. Judged here, at the moment it runs: the poster is still
/// the place's operator (<see cref="Decision"/>), the Offer is open, and the address is the one it offers. Koan's accept
/// sends it through <see cref="Space.PlaceAddress.Use"/>, and holds the origin only once it commits.</summary>
public sealed record UseOffer(string OfferId, string PosterId, string Address, DateTimeOffset? At = null) : ICommand<Offer>
{
    public const string NotOffered = "This is not the address the offer offers.";

    public async Task<Outcome<Offer>> Run(Sequences sequences, CancellationToken ct)
    {
        var (offer, space) = await Decision.Load(OfferId, PosterId, ct);
        if (offer is null || space is null) return new Outcome<Offer>.Refused(Decision.NoOffer);
        if (offer.Kind != OfferKind.Address || !string.Equals(offer.Subject, Address, StringComparison.OrdinalIgnoreCase))
            return new Outcome<Offer>.Refused(NotOffered);

        offer.Accept(At ?? DateTimeOffset.UtcNow);
        if (space.TakeAddress(Address)) await space.Save(ct);
        await offer.Save(ct);
        return new Outcome<Offer>.Done(offer);
    }
}
