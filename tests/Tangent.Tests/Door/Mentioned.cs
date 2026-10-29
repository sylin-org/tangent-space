using Koan.Data.Core;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>Appends the event a mention leaves on the log, one per mentioned participant as its target, the way the
/// command that writes a Post with a mention will: numbered by the writer, inside its transaction.</summary>
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
