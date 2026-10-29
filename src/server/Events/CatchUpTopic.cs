using Koan.Data.Core;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Writer;

namespace Tangent.Events;

/// <summary>Moves a participant's checkpoint on one Topic to the sequence they read it through, so the Topic, its
/// Tangent's page and the door stop marking what they saw as new. Never backwards: a sequence at or behind the
/// checkpoint answers with the checkpoint as it stands and writes nothing. The participant exists and the Topic exists
/// for them, or the command answers as for a missing Topic; the sequence is one of the Topic's, from its root's zero to
/// its last, or the command is refused. The watch flag stays as it was, and no other scope moves: a Tangent's
/// checkpoint stands for every Topic in it, which reading one of them says nothing about.</summary>
public sealed record CatchUpTopic(string ParticipantId, string TopicId, long Through) : ICommand<Checkpoint>
{
    public const string NoTopic = "No topic is here.";
    public const string NoSequence = "No post of this topic has this sequence.";

    public async Task<Outcome<Checkpoint>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Participant.Get(ParticipantId, ct) is null) return new Outcome<Checkpoint>.Refused(NoTopic);
        if (await Post.Get(TopicId, ct) is not { ParentId: null } root) return new Outcome<Checkpoint>.Refused(NoTopic);
        if (!(await Capabilities.Load(ParticipantId, root.TangentId, root, ct)).Exists) return new Outcome<Checkpoint>.Refused(NoTopic);
        if (Through < 0 || Through > root.LastSequence) return new Outcome<Checkpoint>.Refused(NoSequence);

        var scope = Scope.Topic(root.Id);
        var existing = await Checkpoint.Get(Checkpoint.Key(ParticipantId, scope), ct);
        if (existing is not null && existing.Seq >= Through) return new Outcome<Checkpoint>.Done(existing);

        var moved = Checkpoint.At(ParticipantId, scope, Through, existing?.Watch ?? false);
        await moved.Save(ct);
        return new Outcome<Checkpoint>.Done(moved);
    }
}
