using Koan.Web.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Tangent.Offers;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Capture;

/// <summary>An Offer of an address, made the way every one is made, for the captures of the operator's door: fourteen
/// requests arrive on <see cref="Origin"/> through the capture's loopback proxy, which the place believes, and the
/// place, with no URL configured and no address yet, offers it to its operator. For <c>offer-used</c> the operator
/// then uses it, and for <c>offer-declined</c> declines it, as their answers do. Compiled only into the capture host,
/// like <see cref="CaptureSeedController"/>.</summary>
public sealed class OfferSeedController(KoanWebBaseUrl baseUrl, WriteLoop writer, IServer server) : ControllerBase
{
    /// <summary>The public origin the capture's place is reached at; <c>src/client/capture/server.ts</c> names it too.</summary>
    public const string Origin = "https://tangent.example.org";

    private const int Requests = 14;

    [HttpPost("/capture/seed/offer")]
    public async Task<IActionResult> Open(CancellationToken ct)
    {
        await Offered(ct);
        return NoContent();
    }

    [HttpPost("/capture/seed/offer-used")]
    public async Task<IActionResult> Used(CancellationToken ct)
    {
        var offer = await Offered(ct);
        var outcome = await PlaceAddress.Use(baseUrl, new UseOffer(offer.Id, offer.DeciderId, offer.Subject), ct);
        if (outcome is not Outcome<Offer>.Done) throw new InvalidOperationException($"The capture's offer was not used: {outcome}.");
        return NoContent();
    }

    [HttpPost("/capture/seed/offer-declined")]
    public async Task<IActionResult> Declined(CancellationToken ct)
    {
        var offer = await Offered(ct);
        var outcome = await writer.Send(new DeclineOffer(offer.Id, offer.DeciderId), ct);
        if (outcome is not Outcome<Offer>.Done) throw new InvalidOperationException($"The capture's offer was not declined: {outcome}.");
        return NoContent();
    }

    /// <summary>Sends the requests through the loopback proxy, then waits, at most ten seconds, for the Offer the first
    /// of them made.</summary>
    private async Task<Offer> Offered(CancellationToken ct)
    {
        var listening = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault(address => address.StartsWith("http://", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The capture host reports no http address it listens on.");
        var origin = new Uri(Origin);
        using (var client = new HttpClient { BaseAddress = new Uri(listening), Timeout = TimeSpan.FromSeconds(10) })
        {
            for (var request = 0; request < Requests; request++)
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, "/health/live");
                message.Headers.Add("X-Forwarded-Proto", origin.Scheme);
                message.Headers.Add("X-Forwarded-Host", origin.Authority);
                message.Headers.Add("X-Forwarded-For", "203.0.113.9");
                using var response = await client.SendAsync(message, ct);
                response.EnsureSuccessStatusCode();
            }
        }

        var id = Offer.IdFor(OfferKind.Address, Origin);
        for (var wait = 0; wait < 100; wait++)
        {
            if (await Offer.Get(id, ct) is { } offer) return offer;
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
        }

        throw new InvalidOperationException($"No offer of {Origin} was made; the state's settings must leave the URL unconfigured.");
    }
}
