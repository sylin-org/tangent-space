using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace Tangent.Activity;

/// <summary>
/// A compact, durable delivery journal. Call AppendInTransaction only from an already coordinated
/// transaction, then call SignalAfterCommit only after that transaction commits successfully.
/// </summary>
public sealed class ActivityJournal : Entity<ActivityJournal>
{
    private static readonly ActivityUpdates updates = new();

    public long Sequence { get; set; }
    public ActivityKind Kind { get; set; }
    public string TopicKey { get; set; } = "";
    public string TangentKey { get; set; } = "home";
    public string? ActorParticipantId { get; set; }
    public string? TargetParticipantId { get; set; }
    public long? PostSequence { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public static async Task<ActivityJournal> AppendInTransaction(ActivityKind kind, string roomKey,
        string? actorDid = null, string? targetDid = null, string? tangentKey = null,
        long? messageSequence = null, DateTimeOffset? occurredAt = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roomKey);
        var head = await ActivityHead.Get(ActivityHead.Key, ct) ?? new ActivityHead { Id = ActivityHead.Key };
        var entry = new ActivityJournal
        {
            Id = Guid.CreateVersion7().ToString("N"),
            Sequence = checked(head.LastSequence + 1),
            Kind = kind,
            TopicKey = roomKey,
            TangentKey = string.IsNullOrWhiteSpace(tangentKey) ? "home" : tangentKey,
            ActorParticipantId = actorDid,
            TargetParticipantId = targetDid,
            PostSequence = messageSequence,
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow
        };
        head.LastSequence = entry.Sequence;
        head.UpdatedAt = entry.OccurredAt;
        await entry.Save(ct);
        await head.Save(ct);
        return entry;
    }

    public static void SignalAfterCommit() => updates.Pulse();

    internal static ActivityUpdates.Subscription Capture() => updates.Capture();
}
