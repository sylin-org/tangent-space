namespace TangentSpace.Activity;

public sealed record ActivityEvent(string Sequence, string Kind, string RoomKey, string TangentKey,
    string? ActorParticipantId, string? TargetParticipantId, long? MessageSequence, DateTimeOffset OccurredAt);

public sealed record ActivityChannel(string RoomKey, string TangentKey, int UnreadCount, bool UnreadCountCapped,
    int DirectReplies, long LastSequence, long ReadSequence, string Freshness, DateTimeOffset? LastMessageAt);

public sealed record ActivitySnapshot(string Checkpoint, IReadOnlyList<ActivityEvent> Events, string? NextCursor,
    bool HasMore, bool ResetRequired, IReadOnlyList<ActivityChannel> Channels, bool ChannelsTruncated = false,
    string? NextChannelCursor = null, bool ChannelsHasMore = false, bool ChannelsIncomplete = false);

internal sealed record ActivityCursor(string ParticipantId, string Consumer, long After, long? Boundary, DateTimeOffset ExpiresAt);
internal sealed record ActivityChannelCursor(string ParticipantId, string Consumer, int Page, DateTimeOffset ExpiresAt);
