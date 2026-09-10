namespace TangentSpace.Activity;

/// <summary>Pure attention rules shared by the activity service and its tests; access checks always apply after them.</summary>
internal static class AttentionRules
{
    /// <summary>The actor's own contributions never count as unread attention.</summary>
    public static bool CountsForAttention(Conversation.Message message, string did) => message.AuthorDid != did;

    /// <summary>Null-safe mode resolution: an absent or malformed stored setting means All.</summary>
    public static WatchMode Effective(WatchSetting? channel, TangentWatchSetting? tangent = null)
        => Effective(channel?.Mode, tangent?.Mode);

    /// <summary>Channel preference overrides the Tangent default; absent everywhere means All.</summary>
    public static WatchMode Effective(WatchMode? channel, WatchMode? tangent)
    {
        if (channel is { } explicitChannel && Enum.IsDefined(explicitChannel)) return explicitChannel;
        if (tangent is { } inherited && Enum.IsDefined(inherited)) return inherited;
        return WatchMode.All;
    }

    /// <summary>A watched-out channel disappears from overviews and its message markers are not delivered.</summary>
    public static bool DeliversChannel(WatchMode mode) => mode != WatchMode.None;

    public static bool DeliversEvent(WatchMode mode, ActivityKind kind) => kind != ActivityKind.MessageAccepted || mode != WatchMode.None;

    /// <summary>Replies mode reports only direct-reply attention; all other modes keep the unread count.</summary>
    public static int AttentionUnread(WatchMode mode, int unread, int directReplies)
        => mode == WatchMode.Replies ? directReplies : unread;

    /// <summary>Default priority: own direct replies first, then channels the participant engaged with.</summary>
    public static int PriorityTier(int directReplies, long readSequence) => directReplies > 0 ? 0 : readSequence > 0 ? 1 : 2;

    /// <summary>Whether a delivered message marker is itself a reply to the actor's accepted message.
    /// The actor's own reply never counts: own messages are not attention.</summary>
    public static bool IsDirectReply(Conversation.Message? message, Conversation.SourceDecision? parent, string did)
        => message?.Content.ReplyTo is not null && message.AuthorDid != did
            && parent?.Accepted == true && parent.AuthorDid == did;
}
