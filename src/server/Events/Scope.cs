namespace Tangent.Events;

/// <summary>Where a checkpoint looks: the whole place, one Tangent, one Topic, or the participant's notifications. The
/// kind is stated, never inferred from the id, and the string form carries it, so a Tangent's slug and a Topic's Id can
/// never name one scope.</summary>
public sealed class Scope
{
    public ScopeKind Kind { get; set; }

    /// <summary>The Tangent's slug or the Topic's Id; empty for the place and for the notifications.</summary>
    public string Id { get; set; } = "";

    /// <summary>The whole Tangent Space.</summary>
    public static Scope Place => new() { Kind = ScopeKind.Place };

    public static Scope Tangent(string slug) => new() { Kind = ScopeKind.Tangent, Id = slug };

    public static Scope Topic(string topicId) => new() { Kind = ScopeKind.Topic, Id = topicId };

    /// <summary>What is for the participant (<see cref="Events.Notifications"/>) — the events that target them, and the
    /// reports and applications that reach them — on the log's sequence.</summary>
    public static Scope Notifications => new() { Kind = ScopeKind.Notifications };

    public override string ToString() => Kind switch
    {
        ScopeKind.Place => "place",
        ScopeKind.Tangent => "tangent:" + Id,
        ScopeKind.Topic => "topic:" + Id,
        ScopeKind.Notifications => "notifications",
        _ => throw new InvalidOperationException($"No scope has the kind {Kind}.")
    };
}

public enum ScopeKind
{
    Place,
    Tangent,
    Topic,
    Notifications
}
