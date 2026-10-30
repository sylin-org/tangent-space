using Koan.Data.Core;
using Tangent.Writer;

namespace Tangent.Offers;

/// <summary>Declines an Offer, once: what it offers is never offered again, since its Id stays taken. Judged here, at
/// the moment it runs: the poster is still the place's operator (<see cref="Decision"/>) and the Offer is open.</summary>
public sealed record DeclineOffer(string OfferId, string PosterId, DateTimeOffset? At = null) : ICommand<Offer>
{
    public async Task<Outcome<Offer>> Run(Sequences sequences, CancellationToken ct)
    {
        var (offer, _) = await Decision.Load(OfferId, PosterId, ct);
        if (offer is null) return new Outcome<Offer>.Refused(Decision.NoOffer);
        offer.Decline(At ?? DateTimeOffset.UtcNow);
        await offer.Save(ct);
        return new Outcome<Offer>.Done(offer);
    }
}
