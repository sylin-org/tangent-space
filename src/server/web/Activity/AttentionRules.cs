namespace Tangent.Activity;

/// <summary>Pure attention rules shared by the activity service and its tests; access checks always apply after them.</summary>
internal static class AttentionRules
{
    /// <summary>The actor's own contributions never count as unread attention.</summary>
    public static bool CountsForAttention(Conversation.Post post, string did) => post.AuthorParticipantId != did;

    /// <summary>Null-safe mode resolution: an absent or malformed stored setting means All.</summary>
    public static WatchMode Effective(WatchSetting? topic, TangentWatchSetting? tangent = null)
        => Effective(topic?.Mode, tangent?.Mode);

    /// <summary>Topic preference overrides the Tangent default; absent everywhere means All.</summary>
    public static WatchMode Effective(WatchMode? topic, WatchMode? tangent)
    {
        if (topic is { } explicitTopic && Enum.IsDefined(explicitTopic)) return explicitTopic;
        if (tangent is { } inherited && Enum.IsDefined(inherited)) return inherited;
        return WatchMode.All;
    }

    /// <summary>A watched-out topic disappears from overviews and its post markers are not delivered.</summary>
    public static bool DeliversTopic(WatchMode mode) => mode != WatchMode.None;

    public static bool DeliversEvent(WatchMode mode, ActivityKind kind) => kind != ActivityKind.PostAccepted || mode != WatchMode.None;

    /// <summary>Replies mode reports only direct-reply attention; all other modes keep the unread count.</summary>
    public static int AttentionUnread(WatchMode mode, int unread, int directReplies)
        => mode == WatchMode.Replies ? directReplies : unread;

    /// <summary>Default priority: own direct replies first, then topics the participant engaged with.</summary>
    public static int PriorityTier(int directReplies, long readSequence) => directReplies > 0 ? 0 : readSequence > 0 ? 1 : 2;

    /// <summary>Whether a delivered post marker is itself a reply to the actor's accepted post.
    /// The actor's own reply never counts: own posts are not attention.</summary>
    public static bool IsDirectReply(Conversation.Post? post, Conversation.SourceDecision? parent, string did)
        => post?.Content.ReplyTo is not null && post.AuthorParticipantId != did
            && parent?.Accepted == true && parent.AuthorParticipantId == did;
}
