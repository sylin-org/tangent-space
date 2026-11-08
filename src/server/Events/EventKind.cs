namespace Tangent.Events;

/// <summary>What an Event says happened.</summary>
public static class EventKind
{
    public const string PostCreated = "post.created";
    public const string PostEdited = "post.edited";
    public const string PostRemoved = "post.removed";

    /// <summary>A Topic's moderator moved it here from another Tangent (<see cref="Posts.MoveTopic"/>), with its rows of the log
    /// before it; it targets no one, and wakes the Connections, so an article's copy follows it.</summary>
    public const string PostMoved = "post.moved";

    /// <summary>A Topic's moderator deleted it whole (<see cref="Posts.DeleteTopic"/>); it targets no one, and wakes the
    /// Connections, so an article's copy comes down from its receipt.</summary>
    public const string TopicDeleted = "topic.deleted";

    /// <summary>A removed Post's words came back (<see cref="Posts.UndoRemoval"/>); it targets no one, and wakes the
    /// Connections, so an article goes out again.</summary>
    public const string RemovalUndone = "removal.undone";
    public const string Mention = "mention";
    public const string StandingChanged = "standing.changed";
    public const string RoleChanged = "role.changed";
    public const string TangentChanged = "tangent.changed";

    /// <summary>Someone who holds <c>ManageSpace</c> deleted a Tangent with everything in it (<see cref="Tangents.TangentWriting.Deleting"/>):
    /// with its Tangent gone, it reaches no one, as no row of that Tangent does.</summary>
    public const string TangentDeleted = "tangent.deleted";

    /// <summary>Someone who manages a private Tangent let a participant in (<c>given</c>) or declined their ask, for that
    /// participant alone.</summary>
    public const string TangentLet = "tangent.let";
    public const string SpaceChanged = "space.changed";
}
