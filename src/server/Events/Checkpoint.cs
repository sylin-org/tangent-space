using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Tangent.Events;

/// <summary>The last sequence a participant saw in a scope — the whole place, a Tangent or a Topic — and whether they
/// watch it. The only per-participant state. Keyed by participant and scope.</summary>
public sealed class Checkpoint : Entity<Checkpoint>
{
    /// <summary>The sequence of a checkpoint that has seen nothing in its scope yet, as a watch leaves it when it comes
    /// before any reading: every sequence of the log and of a Topic, its root's zero among them, is past it.</summary>
    public const long Nothing = -1;

    public static string Key(string participantId, Scope scope) => participantId + "/" + scope;

    [Index(Name = "ix_checkpoint_participants")]
    public string ParticipantId { get; set; } = "";

    public Scope Scope { get; set; } = Scope.Place;

    public long Seq { get; set; }

    public bool Watch { get; set; }

    public static Checkpoint At(string participantId, Scope scope, long seq, bool watch = false) => new()
    {
        Id = Key(participantId, scope),
        ParticipantId = participantId,
        Scope = scope,
        Seq = seq,
        Watch = watch
    };
}
