using Koan.Data.Core;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Writer;

namespace Tangent.Events;

/// <summary>Sets or clears the watch flag on a participant's checkpoint for a Tangent or a Topic, keeping the sequence
/// the checkpoint holds; a scope they have no checkpoint for gains one that has seen <see cref="Checkpoint.Nothing"/>,
/// so watching marks nothing seen. A flag already as asked answers with the checkpoint as it stands and writes nothing.
/// The participant exists and the Tangent or Topic exists for them, or the command answers as for a missing one; the
/// place is not a scope a participant watches.</summary>
public sealed record SetWatch(string ParticipantId, Scope Scope, bool Watch) : ICommand<Checkpoint>
{
    public const string NoScope = "Nothing to watch is here.";

    public async Task<Outcome<Checkpoint>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Participant.Get(ParticipantId, ct) is null || !await Exists(ct)) return new Outcome<Checkpoint>.Refused(NoScope);

        var existing = await Checkpoint.Get(Checkpoint.Key(ParticipantId, Scope), ct);
        if ((existing?.Watch ?? false) == Watch)
            return new Outcome<Checkpoint>.Done(existing ?? Checkpoint.At(ParticipantId, Scope, Checkpoint.Nothing));

        var flagged = Checkpoint.At(ParticipantId, Scope, existing?.Seq ?? Checkpoint.Nothing, Watch);
        await flagged.Save(ct);
        return new Outcome<Checkpoint>.Done(flagged);
    }

    /// <summary>Whether the Tangent or the Topic the scope names exists for the participant.</summary>
    private async Task<bool> Exists(CancellationToken ct)
    {
        switch (Scope.Kind)
        {
            case ScopeKind.Tangent:
                return await Tangents.Tangent.Get(Scope.Id, ct) is { } tangent
                       && (await Capabilities.Load(ParticipantId, tangent.Id, null, ct)).Exists;
            case ScopeKind.Topic:
                return await Post.Get(Scope.Id, ct) is { ParentId: null } root
                       && (await Capabilities.Load(ParticipantId, root.TangentId, root, ct)).Exists;
            default:
                return false;
        }
    }
}
