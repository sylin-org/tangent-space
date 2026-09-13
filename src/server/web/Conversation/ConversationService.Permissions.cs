using TangentSpace.Authorization;
using TangentSpace.Rooms;

namespace TangentSpace.Conversation;

public sealed partial class ConversationService
{
    private static void RequireTopicCapability(RoomPolicy policy, TopicCapability capability, string denial)
    {
        if (!TopicPermissionEvaluator.Evaluate(policy, capability).Allowed)
            throw new UnauthorizedAccessException(denial);
    }

    /// <summary>Call inside the current-policy callback so both authority and the row are fresh.
    /// Keep read authorization ahead of lookup, including when recovering a terminal receipt.</summary>
    private static async Task<Message> ReadPostForChange(RoomPolicy policy, string roomKey, string messageId, CancellationToken ct)
    {
        RequireTopicCapability(policy, TopicCapability.Read, "This room's current rules do not allow changing posts.");
        var current = await Message.Get(messageId, ct);
        if (current is null || current.RoomKey != roomKey) throw new ArgumentException("Choose a message in this room.");
        return current;
    }

    private static void RequirePostChange(RoomPolicy policy, Message current, bool delete)
    {
        var own = string.Equals(current.AuthorParticipantId, policy.ActorParticipantId, StringComparison.Ordinal);
        var capability = !delete ? TopicCapability.EditOwnPost
            : own ? TopicCapability.DeleteOwnPost : TopicCapability.RemovePost;
        var decision = TopicPermissionEvaluator.EvaluatePost(policy, capability, current.AuthorParticipantId, current.Removed);
        if (decision.Allowed) return;
        if (decision.Reason == TopicPermissionEvaluator.RemovedReason)
            throw new ArgumentException("That post has already been removed.");
        if (!delete && !own)
            throw new UnauthorizedAccessException("Moderators may remove posts but cannot rewrite their authors' words.");
        throw new UnauthorizedAccessException("The current room rules do not allow this post change.");
    }
}
