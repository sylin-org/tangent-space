using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;

namespace Tangent.Posts;

/// <summary>Closes a Topic to replies, or reopens it where <see cref="Close"/> is false: its root holds whether it is closed,
/// by whom and when (<see cref="Post.Close"/>, <see cref="Post.Reopen"/>), and every word in it stays. The Topic exists for
/// the actor, or the command answers that no Topic has the Id; the actor holds <see cref="Bit.Moderate"/> on it, which no
/// one does on a removed root, or is refused with the gate word. Closing a closed Topic or reopening an open one answers it
/// as it stands and writes nothing. Nothing is appended to the log: the root says who closed it and when.</summary>
public sealed record CloseTopic(string TopicId, string ActorId, bool Close = true, DateTimeOffset? At = null) : ICommand<Post>
{
    public async Task<Outcome<Post>> Run(CancellationToken ct)
    {
        if (await Post.Get(TopicId, ct) is not { ParentId: null } root) return new Outcome<Post>.Refused(Reply.NoTopic);
        var capabilities = await Capabilities.Load(ActorId, root.TangentId, root, ct);
        if (!capabilities.Exists) return new Outcome<Post>.Refused(Reply.NoTopic);
        if (capabilities.Gate(Bit.Moderate) is { } gate) return new Outcome<Post>.Refused(gate);
        if (Close ? root.Close(ActorId, At ?? DateTimeOffset.UtcNow) : root.Reopen()) await root.Save(ct);
        return new Outcome<Post>.Done(root);
    }
}
