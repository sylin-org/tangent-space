using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tangent.Activity;

/// <summary>
/// A compact, durable delivery journal. AppendInTransaction runs only inside an already
/// coordinated transaction; the caller signals the bus after that transaction commits.
/// The sequence is the journal's own maximum plus one — there is no counter row — and
/// appends serialize on a process-wide gate because the server is the only writer.
/// </summary>
public sealed class ActivityJournal : Entity<ActivityJournal>
{
    private static readonly SemaphoreSlim AppendGate = new(1, 1);
    private static long lastAssigned;
    private static readonly QueryDefinition LatestFirst = new()
    {
        Page = 1, PageSize = 1,
        Sort = [new SortSpec(new MemberPath(typeof(ActivityJournal), [typeof(ActivityJournal).GetProperty(nameof(Sequence))!], typeof(long), false, -1), true)]
    };

    public long Sequence { get; set; }
    public ActivityKind Kind { get; set; }
    public string TopicKey { get; set; } = "";
    public string TangentKey { get; set; } = "home";
    public string? ActorParticipantId { get; set; }
    public string? TargetParticipantId { get; set; }
    public long? PostSequence { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>The newest sequence in the journal, or zero when nothing has been appended.</summary>
    public static async Task<long> LatestSequence(CancellationToken ct = default)
    {
        using var fresh = EntityContext.NoCache();
        var latest = (await ActivityJournal.Query(_ => true, LatestFirst, ct)).FirstOrDefault();
        return latest?.Sequence ?? 0;
    }

    public static async Task<ActivityJournal> AppendInTransaction(ActivityKind kind, string topicKey,
        string? actorDid = null, string? targetDid = null, string? tangentKey = null,
        long? messageSequence = null, DateTimeOffset? occurredAt = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(topicKey);
        await AppendGate.WaitAsync(ct);
        try
        {
            // Reads and writes ride the caller's transaction: the ambient context. The
            // committed maximum plus the in-process high-water mark is exact even when
            // one transaction appends twice — the first append is not yet visible to reads.
            var committed = await LatestSequence(ct);
            var sequence = checked(Math.Max(committed, System.Threading.Interlocked.Read(ref lastAssigned)) + 1);
            var entry = new ActivityJournal
            {
                Id = Guid.CreateVersion7().ToString("N"),
                Sequence = sequence,
                Kind = kind,
                TopicKey = topicKey,
                TangentKey = string.IsNullOrWhiteSpace(tangentKey) ? "home" : tangentKey,
                ActorParticipantId = actorDid,
                TargetParticipantId = targetDid,
                PostSequence = messageSequence,
                OccurredAt = occurredAt ?? DateTimeOffset.UtcNow
            };
            await entry.Save(ct);
            lastAssigned = sequence;
            await SignalCompile(topicKey, ct);
            return entry;
        }
        finally { AppendGate.Release(); }
    }

    /// <summary>Submits the topic's attention compile while the append gate is still held: the
    /// compile's ledger row enlists in the caller's transaction and commits atomically with the
    /// journal entry, and the worker wakes on commit. A signal that cannot be accepted costs
    /// one stale projection until the topic's next event — it never fails the append.</summary>
    private static async Task SignalCompile(string topicKey, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(topicKey)) return;
        try
        {
            var compile = new CompileTopicAttention { Id = topicKey, TopicKey = topicKey };
            await compile.Job.Submit(action: "", ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            var factory = AppHost.Current?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
            (factory ?? NullLoggerFactory.Instance).CreateLogger(typeof(ActivityJournal))
                .LogWarning(error, "The attention compile signal for {TopicKey} was not accepted; the compiled projection stays on its previous state until the next event.", topicKey);
        }
    }
}
