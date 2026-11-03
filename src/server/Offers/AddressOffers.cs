using Koan.Web.Hosting;
using Tangent.Space;

namespace Tangent.Offers;

/// <summary>The open Offer of an address, computed and never stored: Koan's record of the origins requests arrive on
/// (<see cref="KoanWebOrigins"/>), which writes nothing per request and starts again with the process, less the origins
/// already decided. While no URL is configured and the place's address is local, the origin seen most often, then most
/// recently, among those the public reaches and Koan would take that its operator never decided is open
/// (<see cref="Open"/>): one keyed read of their Offers, taking no lock. Only a decision writes (<see cref="UseOffer"/>,
/// <see cref="DeclineOffer"/>), so a request on a forged <c>Host</c> writes nothing, and declining one opens the
/// next.</summary>
public sealed class AddressOffers(KoanWebBaseUrl baseUrl, KoanWebOrigins origins)
{
    /// <summary>The origin open to <paramref name="space"/>'s operator to use as its address, with what Koan has seen of
    /// it since this process started — how many requests, since when — or null.</summary>
    public async Task<ObservedOrigin?> Open(TangentSpace space, CancellationToken ct)
    {
        if (baseUrl.IsConfigured || !space.AddressIsLocal()) return null;
        var candidates = origins.Seen().Where(Offerable)
            .OrderByDescending(seen => seen.Count).ThenByDescending(seen => seen.LastSeen).ToList();
        if (candidates.Count == 0) return null;
        var decided = (await Offer.Get(candidates.Select(seen => Offer.IdFor(OfferKind.Address, seen.Origin)).ToList(), ct))
            .OfType<Offer>().Select(offer => offer.Subject).ToHashSet(StringComparer.Ordinal);
        return candidates.FirstOrDefault(seen => !decided.Contains(seen.Origin));
    }

    /// <summary>The open Offer as its decider reads it, with its two answers under <paramref name="baseAddress"/>, or
    /// null.</summary>
    public async Task<Door.Offered?> Offered(TangentSpace space, string baseAddress, CancellationToken ct)
        => await Open(space, ct) is { } seen
            ? new Door.Offered(Offer.Word(OfferKind.Address), seen.Origin, seen.Count, seen.FirstSeen,
                OfferController.UseForm(baseAddress, seen.Origin), OfferController.DeclineForm(baseAddress, seen.Origin))
            : null;

    /// <summary>Whether the public reaches <paramref name="seen"/> and Koan would take it as the place's address, so a
    /// person is offered only what can be kept: an http origin of a public address is not, nor, while sign-in names the
    /// place's client from its address, one on a port of its own.</summary>
    private bool Offerable(ObservedOrigin seen)
        => seen.AddressClass == OriginAddressClass.Public && baseUrl.WouldAccept(seen.Origin, out _);
}
