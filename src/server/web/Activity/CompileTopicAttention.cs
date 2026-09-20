using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Jobs;

namespace Tangent.Activity;

/// <summary>The background compile of one Topic's attention projection. Journal appends submit
/// it; idempotency collapses a burst of posts on one topic into a single pending compile, and
/// the gate serializes compiles per topic. The work-item's id is the topic key, so repeated
/// signals reuse one work-item row.</summary>
[JobIdempotent(nameof(TopicKey))]
[JobGate(nameof(TopicKey))]
[JobAction("", MaxAttempts = 3, Timeout = "00:05:00")]
public sealed class CompileTopicAttention : Entity<CompileTopicAttention>, IKoanJob<CompileTopicAttention>
{
    public string TopicKey { get; set; } = "";

    public static async Task Execute(CompileTopicAttention job, JobContext ctx, CancellationToken ct)
    {
        await ctx.Progress(0.1, "Compiling " + job.TopicKey);
        await AttentionCompiler.CompileTopic(job.TopicKey, ctx.Services, ct);
        await ctx.Progress(1, "Compiled " + job.TopicKey);
    }
}
