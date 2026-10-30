using System.Collections.Concurrent;
using Koan.Data.Abstractions;
using Koan.Web.Hosting;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Offers;

/// <summary>Where Offers of an address come from: Koan's record of the origins requests arrive on
/// (<see cref="KoanWebOrigins"/>). When an origin first appears that the public reaches over https, it is offered only
/// where nothing in memory already rules it out — no URL is configured, the place is set up, its address is local, no
/// Offer of an address is open, and that origin was never offered — and then <see cref="MakeOffer"/> is handed to the
/// writer only where its queue has room, never waiting, so a request that brought a new origin goes on at once and no
/// stream of forged hosts piles onto the write path; the command judges the rest. What memory holds comes from the
/// Offers read once at start, bounded by <see cref="KnownLimit"/>, and from the writer's commits, and is read without a
/// lock. An origin left out is caught later: when the place is set up, and when its operator declines an Offer, the
/// origin seen most often among those never offered is offered. Where a URL is configured, nothing is offered and a start
/// lapses what is open. The evidence an Offer shows — how many requests, since when — is read live from Koan's record,
/// which writes nothing per request and starts again with the process.</summary>
public sealed class AddressOffers(KoanWebBaseUrl baseUrl, KoanWebOrigins origins, WriteLoop writer, ILogger<AddressOffers> logger)
{
    /// <summary>The most origins this process remembers as offered; beyond them, the writer judges.</summary>
    public const int KnownLimit = 1024;

    private readonly ConcurrentDictionary<string, byte> _offered = new(StringComparer.Ordinal);
    private volatile bool _setUp;
    private volatile bool _open;

    /// <summary>Begins offering where no URL is configured, from what the store holds, or lapses the open Offers where
    /// one is. Called once, at start; the writer starts beside the start, so a lapse is queued and its outcome
    /// logged.</summary>
    public async Task Start(CancellationToken ct)
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

        _setUp = await TangentSpace.Get(TangentSpace.SingleRow, ct) is not null;
        var known = await Offer.Query(offer => offer.Kind == OfferKind.Address, new QueryDefinition { Page = 1, PageSize = KnownLimit }, ct);
        foreach (var offer in known)
        {
            Remember(offer.Subject);
            if (offer.State == OfferState.Open) _open = true;
        }

        writer.Committed += Committed;
        origins.Appeared += (_, seen) => Appeared(seen);
    }

    /// <summary>Whether Koan would take <paramref name="seen"/> as the place's address and the public reaches it: an
    /// https origin of a public address.</summary>
    public static bool Offerable(ObservedOrigin seen)
        => seen.AddressClass == OriginAddressClass.Public && seen.Origin.StartsWith(Uri.UriSchemeHttps + "://", StringComparison.Ordinal);

    /// <summary>What Koan has seen of <paramref name="origin"/> since this process started, or null.</summary>
    public ObservedOrigin? Evidence(string origin)
        => origins.Seen().FirstOrDefault(seen => string.Equals(seen.Origin, origin, StringComparison.Ordinal));

    /// <summary>When the place has just been set up, or its operator has declined an Offer: offers the origin seen most
    /// often, then most recently, among the offerable ones never offered, through one keyed read of their Offers and one
    /// command, where the writer has room for it.</summary>
    public async Task OfferNext(CancellationToken ct)
    {
        var candidates = origins.Seen().Where(seen => Offerable(seen) && Worth(seen.Origin))
            .OrderByDescending(seen => seen.Count).ThenByDescending(seen => seen.LastSeen).ToList();
        if (candidates.Count == 0) return;
        var offered = (await Offer.Get(candidates.Select(seen => Offer.IdFor(OfferKind.Address, seen.Origin)).ToList(), ct))
            .OfType<Offer>().Select(offer => offer.Subject).ToHashSet(StringComparer.Ordinal);
        if (candidates.FirstOrDefault(seen => !offered.Contains(seen.Origin)) is not { } next) return;
        if (writer.TrySend(new MakeOffer(next.Origin)) is { } sent) Reported(next.Origin, await sent.WaitAsync(ct));
    }

    /// <summary>Whether memory leaves <paramref name="origin"/> worth offering: nothing configured, the place set up, its
    /// address local, no Offer of an address open, and the origin never offered.</summary>
    private bool Worth(string origin)
        => !baseUrl.IsConfigured && _setUp && !_open && TangentSpace.IsLocal(baseUrl.Accepted) && !_offered.ContainsKey(origin);

    private void Appeared(ObservedOrigin seen)
    {
        if (!Offerable(seen) || !Worth(seen.Origin)) return;
        if (writer.TrySend(new MakeOffer(seen.Origin)) is not { } sent)
        {
            logger.LogDebug("{Origin} is not offered now: the writer's queue is full", seen.Origin);
            return;
        }

        _ = sent.ContinueWith(made =>
            Reported(seen.Origin, made.IsCompletedSuccessfully ? made.Result : new Outcome<Offer>.Failed(made.Exception!)), TaskScheduler.Default);
    }

    /// <summary>What the writer committed that changes what is worth offering: the settings row written, an Offer made,
    /// an Offer decided. It runs on the writer and returns at once.</summary>
    private void Committed(Committed commit)
    {
        switch (commit.Command)
        {
            case MakeOffer made:
                Remember(made.Origin);
                _open = true;
                break;
            case UseOffer or DeclineOffer:
                _open = false;
                break;
            case ICommand<TangentSpace>:
                _setUp = true;
                break;
        }
    }

    private void Remember(string origin)
    {
        if (_offered.Count < KnownLimit) _offered.TryAdd(origin, 0);
    }

    private void Reported(string origin, Outcome<Offer> outcome)
    {
        switch (outcome)
        {
            case Outcome<Offer>.Done:
                logger.LogInformation("This place is reached at {Origin}; offered to its operator as its address", origin);
                break;
            case Outcome<Offer>.Refused { Reason: var reason }:
                if (reason == MakeOffer.Made) Remember(origin);
                logger.LogDebug("{Origin} is not offered: {Reason}", origin, reason);
                break;
            case var other:
                logger.LogWarning("Offering {Origin} as this place's address did not complete: {Outcome}", origin, other);
                break;
        }
    }
}
