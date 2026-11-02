using Koan.Data.Core.Commands;
using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;
using Koan.Web.Hosting;
using Microsoft.Extensions.Options;
using Tangent.Offers;

namespace Tangent.Space;

/// <summary>First run, where a sign-in completes: signing in to a place that is not established, as the account
/// <c>Tangent:Operator</c> names by handle or DID or, with none named, as anyone, sends <see cref="Establish"/> under the
/// name the verified handle gives (<see cref="TangentSpace.NameFor"/>), and once it commits, unless a URL is configured,
/// takes the origin the sign-in completed on as the place's address (<see cref="KoanWebBaseUrl.Accept"/>, kept by <see cref="PlaceAddress"/>): loopback on a local install, the
/// public origin on a production one. Where that address is local, the public origin seen most often before setup is
/// offered to the setter at once (<see cref="AddressOffers.OfferNext"/>). Anyone else's sign-in sets nothing up. Of two
/// sign-ins at once the first establishes it and the second is refused, and lands where first run sends it. No
/// step fails the sign-in: each is logged and the next sign-in tries again.</summary>
public sealed class FirstRunSignIn(KoanWebBaseUrl baseUrl, AddressOffers offers,
    IOptions<TangentSpaceOptions> options, ILogger<FirstRunSignIn> logger) : IKoanAuthFlowHandler
{
    /// <summary>After the participant's own sign-in step, which runs at 1100 and records their arrival.</summary>
    public int Priority => 1200;

    public async Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return;
        try
        {
            if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not null) return;
            var handle = ctx.Identity.FindFirst(AtprotoClaimTypes.Handle)?.Value ?? "";
            if (!options.Value.MaySetUp(ctx.UserId, handle)) return;

            switch (await new Establish(ctx.UserId, handle, TangentSpace.NameFor(handle)).Send())
            {
                case Outcome<TangentSpace>.Done { Result: var space }:
                    logger.LogInformation("{Person} set up this Tangent Space as {Name} at sign-in", ctx.UserId, space.Name);
                    await TakeAddress(ctx.HttpContext, ct);
                    await offers.OfferNext(ct);
                    break;
                case Outcome<TangentSpace>.Refused { Reason: var refused } when refused == Establish.AlreadyEstablished:
                    break;
                case var other:
                    logger.LogWarning("First run at the sign-in of {Person} did not complete: {Outcome}", ctx.UserId, other);
                    break;
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(failure, "First run at the sign-in of {Person} failed", ctx.UserId);
        }
    }

    /// <summary>The origin this sign-in completed on becomes the place's address, unless a URL is configured. One Koan
    /// refuses to take, such as an http origin outside Development, is logged, and the place answers at each request's
    /// own origin until its operator uses one it is offered.</summary>
    private async Task TakeAddress(HttpContext http, CancellationToken ct)
    {
        if (baseUrl.IsConfigured) return;
        var origin = baseUrl.Resolve(http);
        try
        {
            await baseUrl.Accept(origin, ct);
        }
        catch (ArgumentException refused)
        {
            logger.LogWarning("This Tangent Space was set up at {Origin}, which it cannot take as its address: {Reason}", origin, refused.Message);
        }
    }
}
