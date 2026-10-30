using Tangent.Space;

namespace Tangent.Offers;

/// <summary>What deciding an Offer reads, inside the decision's command: the Offer and the place, where the one who
/// decides is still the place's operator. For anyone else, and for an Offer that does not exist, there is nothing to
/// decide (<see cref="NoOffer"/>), as for a missing resource.</summary>
public static class Decision
{
    public const string NoOffer = "There is no such offer for you to decide.";

    public static async Task<(Offer? Offer, TangentSpace? Space)> Load(string offerId, string posterId, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space || space.OperatorId != posterId) return (null, null);
        return await Offer.Get(offerId, ct) is { } offer ? (offer, space) : (null, null);
    }
}
