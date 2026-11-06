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

    /// <summary>Someone who holds <c>ManageSpace</c> deleted a Tangent with everything in it (<see cref="Tangents.DeleteTangent"/>):
    /// with its Tangent gone, it reaches no one, as no row of that Tangent does.</summary>
    public const string TangentDeleted = "tangent.deleted";

    /// <summary>Someone who manages a private Tangent let a participant in (<c>given</c>) or declined their ask, for that
    /// participant alone.</summary>
    public const string TangentLet = "tangent.let";
    public const string SpaceChanged = "space.changed";
}
