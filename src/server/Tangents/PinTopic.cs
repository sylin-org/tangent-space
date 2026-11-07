using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Posts;

namespace Tangent.Tangents;

/// <summary>Pins a Topic at the top of its Tangent, in place of the one pinned before, or unpins it where <see cref="Pin"/>
/// is false: the Tangent holds the one it keeps there (<see cref="Tangent.PinnedId"/>). The Topic exists for the actor, or
/// the command answers that no Topic has the Id; the actor holds <see cref="Bit.Moderate"/> on it, which no one does on a
/// removed root, or is refused with the gate word. Pinning the pinned Topic or unpinning another answers the Tangent as it
/// stands and writes nothing. Nothing is appended to the log.</summary>
public sealed record PinTopic(string TopicId, string ActorId, bool Pin = true) : ICommand<Tangent>
{
    public async Task<Outcome<Tangent>> Run(CancellationToken ct)
    {
        if (await Post.Get(TopicId, ct) is not { ParentId: null } root) return new Outcome<Tangent>.Refused(Post.NoTopic);
        var capabilities = await Capabilities.Load(ActorId, root.TangentId, root, ct);
        if (!capabilities.Exists || await Tangent.Get(root.TangentId, ct) is not { } tangent) return new Outcome<Tangent>.Refused(Post.NoTopic);
        if (capabilities.Gate(Bit.Moderate) is { } gate) return new Outcome<Tangent>.Refused(gate);
        if (Pin ? tangent.Pin(root.Id) : tangent.Unpin(root.Id)) await tangent.Save(ct);
        return new Outcome<Tangent>.Done(tangent);
    }
}
