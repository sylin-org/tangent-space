using Koan.Web.Hosting;
using Tangent.Writer;

namespace Tangent.Offers;

/// <summary>Where Offers of an address come from: Koan's record of the origins requests arrive on
/// (<see cref="KoanWebOrigins"/>). When an origin first appears that the public reaches over https, and no URL is
/// configured, it hands <see cref="MakeOffer"/> to the writer without waiting, so the request that brought it goes on
/// at once; the command judges the rest. When the operator declines one, the origin seen most often among those never
/// offered is offered next, so an origin that appeared while another was open is not lost. Where a URL is configured,
/// nothing is offered and a start lapses what is open. The evidence an Offer shows — how many requests, since when — is
/// read live from Koan's record, which writes nothing per request and starts again with the process.</summary>
public sealed class AddressOffers(KoanWebBaseUrl baseUrl, KoanWebOrigins origins, WriteLoop writer, ILogger<AddressOffers> logger)
{
    /// <summary>Begins offering where no URL is configured, or lapses the open Offers where one is. Called once, at start;
    /// the writer starts beside the start, so a lapse is queued and its outcome logged.</summary>
    public void Start()
    {
        if (baseUrl.IsConfigured)
        {
            _ = writer.Send(new LapseOffers(), CancellationToken.None).ContinueWith(sent =>
            {
                switch (sent.IsCompletedSuccessfully ? sent.Result : new Outcome<int>.Failed(sent.Exception!))
                {
                    case Outcome<int>.Done { Result: > 0 and var lapsed }:
                        logger.LogInformation("A URL is configured, so {Count} open offers lapsed", lapsed);
                        break;
                    case Outcome<int>.Done:
                        break;
                    case var other:
                        logger.LogWarning("Open offers could not lapse: {Outcome}", other);
                        break;
                }
            }, TaskScheduler.Default);
            return;
        }

        origins.Appeared += (_, seen) => Appeared(seen);
    }

    /// <summary>Whether Koan would take <paramref name="seen"/> as the place's address and the public reaches it: an
    /// https origin of a public address.</summary>
    public static bool Offerable(ObservedOrigin seen)
        => seen.AddressClass == OriginAddressClass.Public && seen.Origin.StartsWith(Uri.UriSchemeHttps + "://", StringComparison.Ordinal);

    /// <summary>What Koan has seen of <paramref name="origin"/> since this process started, or null.</summary>
    public ObservedOrigin? Evidence(string origin)
        => origins.Seen().FirstOrDefault(seen => string.Equals(seen.Origin, origin, StringComparison.Ordinal));

    /// <summary>After a decline: offers the origin seen most often, then most recently, among the offerable ones never
    /// offered, through one keyed read of their Offers and one command.</summary>
    public async Task OfferNext(CancellationToken ct)
    {
        if (baseUrl.IsConfigured) return;
        var candidates = origins.Seen().Where(Offerable).OrderByDescending(seen => seen.Count).ThenByDescending(seen => seen.LastSeen).ToList();
        if (candidates.Count == 0) return;
        var offered = (await Offer.Get(candidates.Select(seen => Offer.IdFor(OfferKind.Address, seen.Origin)).ToList(), ct))
            .OfType<Offer>().Select(offer => offer.Subject).ToHashSet(StringComparer.Ordinal);
        if (candidates.FirstOrDefault(seen => !offered.Contains(seen.Origin)) is not { } next) return;
        Reported(next.Origin, await writer.Send(new MakeOffer(next.Origin), ct));
    }

    private void Appeared(ObservedOrigin seen)
    {
        if (!Offerable(seen)) return;
        _ = writer.Send(new MakeOffer(seen.Origin), CancellationToken.None).ContinueWith(sent =>
            Reported(seen.Origin, sent.IsCompletedSuccessfully ? sent.Result : new Outcome<Offer>.Failed(sent.Exception!)), TaskScheduler.Default);
    }

    private void Reported(string origin, Outcome<Offer> outcome)
    {
        switch (outcome)
        {
            case Outcome<Offer>.Done:
                logger.LogInformation("This place is reached at {Origin}; offered to its operator as its address", origin);
                break;
            case Outcome<Offer>.Refused { Reason: var reason }:
                logger.LogDebug("{Origin} is not offered: {Reason}", origin, reason);
                break;
            case var other:
                logger.LogWarning("Offering {Origin} as this place's address did not complete: {Outcome}", origin, other);
                break;
        }
    }
}
