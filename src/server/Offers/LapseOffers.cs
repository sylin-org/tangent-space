using Koan.Data.Abstractions;
using Koan.Data.Core;
using Tangent.Writer;

namespace Tangent.Offers;

/// <summary>Lapses the open Offers, at most <see cref="Offer.OpenLimit"/> of them: what a start sends where a URL is
/// configured, which wins over any address an Offer could give. Answers how many lapsed.</summary>
public sealed record LapseOffers(DateTimeOffset? At = null) : ICommand<int>
{
    public async Task<Outcome<int>> Run(Sequences sequences, CancellationToken ct)
    {
        var open = await Offer.Query(offer => offer.State == OfferState.Open, new QueryDefinition { Page = 1, PageSize = Offer.OpenLimit }, ct);
        var at = At ?? DateTimeOffset.UtcNow;
        foreach (var offer in open)
        {
            offer.Lapse(at);
            await offer.Save(ct);
        }

        return new Outcome<int>.Done(open.Count);
    }
}
