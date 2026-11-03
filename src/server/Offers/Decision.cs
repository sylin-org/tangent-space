using Koan.Data.Core.Commands;
using Tangent.Space;

namespace Tangent.Offers;

/// <summary>What deciding an Offer of an address reads, inside the decision's command: the place, where the one who
/// decides is still its operator, and no decision of that origin yet. For anyone else there is nothing to decide
/// (<see cref="NoOffer"/>), as for a missing resource; an origin decided before is no longer open
/// (<see cref="Offer.NotOpen"/>).</summary>
public static class Decision
{
    public static readonly Reason NoOffer = Reason.Absent("There is no such offer for you to decide.");

    public static async Task<TangentSpace> Open(string origin, string posterId, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space || space.OperatorId != posterId) throw new Refusal(NoOffer);
        if (await Offer.Get(Offer.IdFor(OfferKind.Address, origin), ct) is not null) throw new Refusal(Offer.NotOpen);
        return space;
    }
}
