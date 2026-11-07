using Koan.Data.Core;
using Koan.Data.Core.Commands;

namespace Tangent.Offers;

/// <summary>Declines the Offer of <see cref="Origin"/> as the place's address, once: the origin is never offered again,
/// since the decision's Id stays taken. Judged here, at the moment it runs: the poster is still the place's operator
/// and the origin was never decided (<see cref="Decision"/>).</summary>
public sealed record DeclineOffer(string Origin, string PosterId) : ICommand<Offer>
{
    public async Task<Outcome<Offer>> Run(CancellationToken ct)
    {
        await Decision.Open(Origin, PosterId, ct);
        var declined = await Offer.Decided(OfferKind.Address, Origin, OfferState.Declined).Save(ct);
        return new Outcome<Offer>.Done(declined);
    }
}
