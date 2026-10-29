using Koan.Data.Core;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>Appends a mention event on its own, numbered by the writer inside its transaction, for a proof that needs one
/// the commands that write a Post would not write: a mention whose target may no longer view its Tangent.</summary>
public sealed record Mentioned(string TangentId, string TopicId, string PostId, string ActorId, string TargetId) : ICommand<Event>
{
    public async Task<Outcome<Event>> Run(Sequences sequences, CancellationToken ct)
    {
        var mention = Event.Next(sequences, EventKind.Mention, DateTimeOffset.UtcNow,
            tangentId: TangentId, topicId: TopicId, postId: PostId, actorId: ActorId, targetId: TargetId);
        await mention.Save(ct);
        return new Outcome<Event>.Done(mention);
    }
}
