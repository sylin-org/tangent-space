using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Jobs;
using Tangent.Community;

namespace Tangent.Activity;

/// <summary>Compiles attention for every Topic once per boot, so a server restart repopulates
/// the projection from truth without waiting for new events. Per-topic compilation is shared
/// with the event-time compile; the boot sweep only enumerates.</summary>
[JobAction("", Schedule = "@boot", MaxAttempts = 1, Timeout = "00:30:00")]
public sealed class AttentionBackfill : Entity<AttentionBackfill>, IKoanJob<AttentionBackfill>
{
    public static async Task Execute(AttentionBackfill job, JobContext ctx, CancellationToken ct)
    {
        var compiled = 0;
        var page = 1;
        IReadOnlyList<Topic> batch;
        do
        {
            batch = await Topic.Query(_ => true, PageOf(page), ct);
            foreach (var topic in batch)
            {
                await AttentionCompiler.CompileTopic(topic.Id, ctx.Services, ct);
                compiled++;
                // The total is unknown until the enumeration ends; the fraction approaches
                // completion without ever claiming it early.
                await ctx.Progress(Math.Min(0.99, compiled / (double)(compiled + 4)), "Backfilled " + topic.Id);
            }
            page++;
        }
        while (batch.Count >= BackfillPageSize);
    }

    private const int BackfillPageSize = 50;

    private static QueryDefinition PageOf(int page)
    {
        var member = typeof(Topic).GetProperty(nameof(Topic.Id))!;
        return new QueryDefinition
        {
            Page = page, PageSize = BackfillPageSize,
            Sort = [new SortSpec(new MemberPath(typeof(Topic), [member], member.PropertyType, false, -1), false)]
        };
    }
}
