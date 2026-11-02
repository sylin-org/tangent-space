using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Events;

namespace Tangent.Space;

/// <summary>Appends a mention event on its own, numbered inside its own hold, for a proof that needs one
/// the commands that write a Post would not write: a mention whose target may no longer view its Tangent.</summary>
public sealed record Mentioned(string TangentId, string TopicId, string PostId, string ActorId, string TargetId) : ICommand<Event>
{
    public async Task<Outcome<Event>> Run(CancellationToken ct)
    {
        return new Outcome<Event>.Done(await Event.Append(EventKind.Mention, DateTimeOffset.UtcNow, ct,
            tangentId: TangentId, topicId: TopicId, postId: PostId, actorId: ActorId, targetId: TargetId));
    }
}
