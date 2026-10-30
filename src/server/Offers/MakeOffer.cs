using Koan.Data.Abstractions;
using Koan.Data.Core;
using Tangent.Events;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Offers;

/// <summary>Offers the place's operator <see cref="Origin"/>, an https origin the public reaches it at, as its address,
/// and appends <see cref="EventKind.OfferMade"/> targeted at the operator, who decides it. Judged here, at the moment it
/// runs: the place is set up, its address is local (<see cref="TangentSpace.AddressIsLocal"/>), no Offer of that origin
/// was ever made, and no Offer of an address is open, so a forged <c>Host</c> fills no more than one line of the
/// operator's door. Whether a URL is configured, and whether the origin is one Koan accepts, is the sender's
/// question.</summary>
public sealed record MakeOffer(string Origin, DateTimeOffset? At = null) : ICommand<Offer>
{
    public const string NoPlace = "This Tangent Space is not set up yet, so it has no operator to offer an address to.";

    public const string NotLocal = "This place's address is not local, so no other address is offered.";

    public const string Made = "This origin was offered before.";

    public const string OneOpen = "An offer of an address is already open.";

    public async Task<Outcome<Offer>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { OperatorId: { } operatorId } space)
            return new Outcome<Offer>.Refused(NoPlace);
        if (!space.AddressIsLocal()) return new Outcome<Offer>.Refused(NotLocal);
        var id = Offer.IdFor(OfferKind.Address, Origin);
        if (await Offer.Get(id, ct) is not null) return new Outcome<Offer>.Refused(Made);
        var open = await Offer.Query(offer => offer.State == OfferState.Open && offer.Kind == OfferKind.Address,
            new QueryDefinition { Page = 1, PageSize = 1 }, ct);
        if (open.Count > 0) return new Outcome<Offer>.Refused(OneOpen);

        var at = At ?? DateTimeOffset.UtcNow;
        var made = Offer.Make(OfferKind.Address, Origin, operatorId, at);
        await made.Save(ct);
        await Event.Next(sequences, EventKind.OfferMade, at, targetId: operatorId, offerId: made.Id).Save(ct);
        return new Outcome<Offer>.Done(made);
    }
}
