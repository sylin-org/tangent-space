namespace Tangent.Activity;

public sealed record ActivityEvent(string Sequence, string Kind, string TopicKey, string TangentKey,
    string? ActorParticipantId, string? TargetParticipantId, long? PostSequence, DateTimeOffset OccurredAt);

public sealed record ActivityTopic(string TopicKey, string TangentKey, int UnreadCount, bool UnreadCountCapped,
    int DirectReplies, long LastSequence, long ReadSequence, DateTimeOffset? LastPostAt);

public sealed record ActivitySnapshot(string Checkpoint, IReadOnlyList<ActivityEvent> Events, string? NextCursor,
    bool HasMore, bool ResetRequired, IReadOnlyList<ActivityTopic> Topics, bool TopicsTruncated = false,
    string? NextTopicCursor = null, bool TopicsHasMore = false, bool TopicsIncomplete = false)
{
    /// <summary>Response context stamped by the authenticated browser endpoint, never caller authority.
    /// A browser must re-resolve its identity before applying a response for another participant.</summary>
    public string? ParticipantRef { get; init; }
}

internal sealed record ActivityCursor(string ParticipantId, string Consumer, long After, long? Boundary, DateTimeOffset ExpiresAt);
internal sealed record ActivityTopicCursor(string ParticipantId, string Consumer, int Page, DateTimeOffset ExpiresAt);
