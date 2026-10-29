using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Tangent.Events;

/// <summary>The last sequence a participant saw in a scope — the whole place, a Tangent or a Topic — and whether they
/// watch it. The only per-participant state. Keyed by participant and scope.</summary>
public sealed class Checkpoint : Entity<Checkpoint>
{
    public static string Key(string participantId, string scopeId) => participantId + "/" + scopeId;

    [Index(Name = "ix_checkpoint_participants")]
    public string ParticipantId { get; set; } = "";

    /// <summary>A Tangent's slug, a Topic's Id, or the Tangent Space's row Id.</summary>
    public string ScopeId { get; set; } = "";

    public long Seq { get; set; }

    public bool Watch { get; set; }

    public static Checkpoint At(string participantId, string scopeId, long seq, bool watch = false) => new()
    {
        Id = Key(participantId, scopeId),
        ParticipantId = participantId,
        ScopeId = scopeId,
        Seq = seq,
        Watch = watch
    };
}
