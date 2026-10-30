namespace Tangent.Events;

/// <summary>What an Event says happened.</summary>
public static class EventKind
{
    public const string PostCreated = "post.created";
    public const string PostEdited = "post.edited";
    public const string PostRemoved = "post.removed";
    public const string PostMoved = "post.moved";
    public const string Mention = "mention";
    public const string StandingChanged = "standing.changed";
    public const string RoleChanged = "role.changed";
    public const string TangentChanged = "tangent.changed";
    public const string SpaceChanged = "space.changed";
    public const string OfferMade = "offer.made";
}
