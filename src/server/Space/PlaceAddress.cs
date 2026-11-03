using Koan.Data.Core.Commands;
using Koan.Web.Hosting;
using Tangent.Offers;

namespace Tangent.Space;

/// <summary>Where Koan keeps the URL the place accepts: the row's <see cref="TangentSpace.Address"/>. Koan reads it once
/// before the host serves and saves through it whenever the place accepts an origin. Until one is accepted, and while no
/// URL is configured, Koan takes each request's base URL from the request itself, in Production too
/// (<see cref="DerivesUntilAccepted"/>), so a place is set up on the address its first run is reached at. A plain accept
/// keeps the address (<see cref="KeepAddress"/>); an accept that uses an Offer (<see cref="Use"/>) runs that Offer's own
/// command, which judges it, keeps the address and accepts the Offer in one commit. Either finishes whatever becomes of
/// the request that asked for it.</summary>
public sealed class PlaceAddress : IApplicationUrlStore
{
    /// <summary>The Offer being used on this flow, which <see cref="Save"/> runs in place of a plain keep.</summary>
    private static readonly AsyncLocal<Using?> Pending = new();

    public bool DerivesUntilAccepted => true;

    public async Task<string?> Load(CancellationToken ct) => (await TangentSpace.Get(TangentSpace.SingleRow, ct))?.Address;

    public async Task Save(string applicationUrl, CancellationToken ct)
    {
        if (Pending.Value is { } use)
        {
            use.Outcome = await (use.Command with { Address = applicationUrl }).Send();
            if (use.Outcome is not Outcome<Offer>.Done)
                throw new InvalidOperationException($"Using the offer of {use.Command.Origin} kept nothing: {use.Outcome}.");
            return;
        }

        if (await new KeepAddress(applicationUrl).Send() is not Outcome<TangentSpace>.Done and var kept)
            throw new InvalidOperationException($"Keeping {applicationUrl} as the place's address was not done: {kept}.");
    }

    /// <summary>Uses an Offer of an address, in this order: Koan validates the origin; <see cref="Save"/> sends
    /// <paramref name="command"/>, which judges the Offer and, in one commit, keeps the address and accepts the Offer; only
    /// once that commits does Koan hold the origin. A refusal or a failure at any step leaves the Offer open and the address
    /// unchanged, and an origin Koan will not take is refused in its words. Answers the command's outcome.</summary>
    public static async Task<Outcome<Offer>> Use(KoanWebBaseUrl baseUrl, UseOffer command)
    {
        var use = new Using(command);
        Pending.Value = use;
        try
        {
            await baseUrl.Accept(command.Address);
        }
        catch (ArgumentException refused)
        {
            return new Outcome<Offer>.Refused(Reason.Conflict(refused.Message));
        }
        catch (InvalidOperationException) when (use.Outcome is not null)
        {
            // Save said so; the outcome says why.
        }
        finally
        {
            Pending.Value = null;
        }

        return use.Outcome ?? throw new InvalidOperationException("Koan held the origin without keeping it.");
    }

    private sealed class Using(UseOffer command)
    {
        public UseOffer Command => command;

        public Outcome<Offer>? Outcome { get; set; }
    }
}
