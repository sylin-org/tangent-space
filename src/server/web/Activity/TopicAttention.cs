using Koan.Cache.Abstractions.Policies;
using Koan.Data.Core.Model;

namespace Tangent.Activity;

/// <summary>The compiled attention projection for one participant over one Topic. Background
/// compiles write it when journal events signal a change; the digest and the activity overview
/// read it instead of recomputing attention per request. A row absent means no attention is
/// known — a topic with no compiled activity contributes nothing to a viewer's digest.</summary>
/// <remarks>The id composes the two key parts, <c>topicKey|participantId</c>, mirroring the
/// composite-key convention of the membership entities.</remarks>
[Cacheable(60)]
public sealed class TopicAttention : Entity<TopicAttention>
{
    public string TopicKey { get; set; } = "";
    public string ParticipantId { get; set; } = "";

    /// <summary>Unread posts that count for attention — the viewer's own writes excluded.</summary>
    public int UnreadCount { get; set; }

    /// <summary>Unread posts answering one of the viewer's accepted posts.</summary>
    public int DirectReplies { get; set; }

    /// <summary>The Topic's last accepted sequence when this row was compiled.</summary>
    public long LastSequence { get; set; }

    /// <summary>The viewer's acknowledged sequence when this row was compiled; the unread
    /// window is everything above it.</summary>
    public long ReadSequence { get; set; }

    /// <summary>When the Topic's newest sequence was accepted; null for a Topic with no posts.</summary>
    public DateTimeOffset? LastPostAt { get; set; }

    public DateTimeOffset CompiledAt { get; set; }

    public static string IdOf(string topicKey, string participantId) => topicKey + "|" + participantId;
}
