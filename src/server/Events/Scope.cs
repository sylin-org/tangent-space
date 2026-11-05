namespace Tangent.Events;

/// <summary>Where a checkpoint looks: the whole place, counted in the log's sequence, or one Topic, counted in that
/// Topic's own. The kind is stated, never inferred from the id, and the string form carries it.</summary>
public sealed class Scope
{
    public ScopeKind Kind { get; set; }

    /// <summary>The Topic's Id; empty for the place.</summary>
    public string Id { get; set; } = "";

    /// <summary>The whole Tangent Space.</summary>
    public static Scope Place => new() { Kind = ScopeKind.Place };

    public static Scope Topic(string topicId) => new() { Kind = ScopeKind.Topic, Id = topicId };

    public override string ToString() => Kind switch
    {
        ScopeKind.Place => "place",
        ScopeKind.Topic => "topic:" + Id,
        _ => throw new InvalidOperationException($"No scope has the kind {Kind}.")
    };
}

public enum ScopeKind
{
    Place,
    Topic
}
