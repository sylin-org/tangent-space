using Koan.Web.Hosting;
using Tangent.Offers;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>Where Koan keeps the URL the place accepts: the row's <see cref="TangentSpace.Address"/>. Koan reads it once
/// before the host serves and saves through it whenever the place accepts an origin. Until one is accepted, and while no
/// URL is configured, Koan takes each request's base URL from the request itself, in Production too
/// (<see cref="DerivesUntilAccepted"/>), so a place is set up on the address its first run is reached at. Only the writer
/// writes: a plain accept keeps the address (<see cref="KeepAddress"/>); an accept that uses an Offer
/// (<see cref="Use"/>) runs that Offer's own command, which judges it, keeps the address and accepts the Offer in one
/// transaction.</summary>
public sealed class PlaceAddress(WriteLoop writer) : IApplicationUrlStore
{
    /// <summary>The Offer being used on this flow, which <see cref="Save"/> runs in place of a plain keep.</summary>
    private static readonly AsyncLocal<Using?> Pending = new();

    public bool DerivesUntilAccepted => true;

    public async Task<string?> Load(CancellationToken ct) => (await TangentSpace.Get(TangentSpace.SingleRow, ct))?.Address;

    public async Task Save(string applicationUrl, CancellationToken ct)
    {
        if (Pending.Value is { } use)
        {
            use.Outcome = await writer.Send(use.Command with { Address = applicationUrl }, ct);
            if (use.Outcome is not Outcome<Offer>.Done)
                throw new InvalidOperationException($"Using the offer {use.Command.OfferId} kept nothing: {use.Outcome}.");
            return;
        }

        switch (await writer.Send(new KeepAddress(applicationUrl), ct))
        {
            case Outcome<TangentSpace>.Done:
                return;
            case Outcome<TangentSpace>.Failed { Cause: var cause }:
                throw new InvalidOperationException($"Keeping {applicationUrl} as the place's address failed.", cause);
            case var other:
                throw new InvalidOperationException($"Keeping {applicationUrl} as the place's address was not done: {other}.");
        }
    }

    /// <summary>Uses an Offer of an address, in this order: Koan validates the origin; <see cref="Save"/> hands
    /// <paramref name="command"/> to the writer, which judges the Offer and, in one transaction, keeps the address and
    /// accepts the Offer; only once that commits does Koan hold the origin. A refusal or a failure at any step leaves the
    /// Offer open and the address unchanged. Answers the command's outcome, or the failure Koan met before it ran.</summary>
    public static async Task<Outcome<Offer>> Use(KoanWebBaseUrl baseUrl, UseOffer command, CancellationToken ct)
    {
        var use = new Using(command);
        Pending.Value = use;
        try
        {
            await baseUrl.Accept(command.Address, ct);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return use.Outcome is { } outcome and not Outcome<Offer>.Done ? outcome : new Outcome<Offer>.Failed(failure);
        }
        finally
        {
            Pending.Value = null;
        }

        return use.Outcome ?? new Outcome<Offer>.Failed(new InvalidOperationException("Koan held the origin without keeping it."));
    }

    private sealed class Using(UseOffer command)
    {
        public UseOffer Command => command;

        public Outcome<Offer>? Outcome { get; set; }
    }
}
