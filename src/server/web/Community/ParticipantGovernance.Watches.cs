using Koan.Data.Core;
using Koan.Jobs;
using Tangent.Activity;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Stewardship;

namespace Tangent.Community;

public sealed partial class ParticipantGovernance
{
    /// <summary>Personal interest only: never joins, never changes admission, never touches read positions or access.</summary>
    public async Task<WatchResult> SetWatch(string actorId, string topicKey, WatchMode mode, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            var read = await topics.ReadAsync(actorId, topicKey, ct);
            var policy = read.Policy;
            if (!policy.CanRead)
                throw new TangentRuleViolation(TangentDenial.Forbidden, "Watch state needs read access to the topic.");
            var setting = WatchSetting.Choose(actorId, topicKey, mode, actorId, clock.GetUtcNow());
            await setting.Save(ct);
            await new CompileTopicAttention { Id = topicKey, TopicKey = topicKey }.Job.Submit(action: "", ct);
            await EntityContext.Commit(ct);
            return new WatchResult(topicKey, setting.Mode);
        }
        finally { gate.Exit(); }
    }

    /// <summary>Personal Tangent-wide default; each topic's explicit watch preference still overrides it.</summary>
    public async Task<WatchResult> SetTangentWatch(string actorId, string tangentKey, WatchMode mode, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tangentKey);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
            // A Tangent default is a preference, not an access change: no policy check, just an active arrival.
            var participant = await Participant.Get(actorId, ct);
            if (participant is null || participant.IsSuspended)
                throw new TangentRuleViolation(TangentDenial.Forbidden, "An active verified arrival is required.");
            _ = await Community.Tangent.Get(tangentKey, ct)
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
            var setting = TangentWatchSetting.Choose(actorId, tangentKey, mode, actorId, clock.GetUtcNow());
            await setting.Save(ct);
            await EntityContext.Commit(ct);
            return new WatchResult(tangentKey, setting.Mode);
        }
        finally { gate.Exit(); }
    }
}
