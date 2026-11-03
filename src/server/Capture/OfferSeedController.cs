using Koan.Data.Core.Commands;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Tangent.Offers;
using Tangent.Space;

namespace Tangent.Capture;

/// <summary>An Offer of an address, open the way every one is, for the captures of the operator's door: fourteen
/// requests arrive on <see cref="Origin"/> through the capture's loopback proxy, which the place believes, and the
/// place, with no URL configured and no address yet, offers it to its operator. For <c>offer-used</c> the operator
/// then uses it, and for <c>offer-declined</c> declines it, as their answers do. Compiled only into the capture host,
/// like <see cref="CaptureSeedController"/>.</summary>
public sealed class OfferSeedController(KoanWebBaseUrl baseUrl, IServer server) : ControllerBase
{
    /// <summary>The public origin the capture's place is reached at; <c>src/client/capture/server.ts</c> names it too.</summary>
    public const string Origin = "https://tangent.example.org";

    private const int Requests = 14;

    [HttpPost("/capture/seed/offer")]
    public async Task<IActionResult> Open(CancellationToken ct)
    {
        await Reached(ct);
        return NoContent();
    }

    [HttpPost("/capture/seed/offer-used")]
    public async Task<IActionResult> Used(CancellationToken ct)
    {
        var operatorId = await Reached(ct);
        var outcome = await PlaceAddress.Use(baseUrl, new UseOffer(Origin, operatorId, Origin));
        if (outcome is not Outcome<Offer>.Done) throw new InvalidOperationException($"The capture's offer was not used: {outcome}.");
        return NoContent();
    }

    [HttpPost("/capture/seed/offer-declined")]
    public async Task<IActionResult> Declined(CancellationToken ct)
    {
        var operatorId = await Reached(ct);
        var outcome = await new DeclineOffer(Origin, operatorId).Send();
        if (outcome is not Outcome<Offer>.Done) throw new InvalidOperationException($"The capture's offer was not declined: {outcome}.");
        return NoContent();
    }

    /// <summary>Sends the requests through the loopback proxy, and answers the place's operator, who decides the Offer
    /// they open.</summary>
    private async Task<string> Reached(CancellationToken ct)
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

        return (await TangentSpace.Get(TangentSpace.SingleRow, ct))?.OperatorId
            ?? throw new InvalidOperationException("The capture's place is not set up, so no offer is open to anyone.");
    }
}
