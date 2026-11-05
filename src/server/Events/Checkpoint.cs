using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Tangent.Events;

/// <summary>A participant's position in one scope: in the place, the last sequence of the log they said they had seen;
/// in a Topic, the last of that Topic's own sequence. Keyed by participant and scope. A position moves forward only, by
/// <see cref="Seen"/> and by its participant's own write in a Topic they had read to its end
/// (<see cref="Posts.StartTopic"/>, <see cref="Posts.Reply"/>); what is new to them is <see cref="Unread"/>'s.</summary>
public sealed class Checkpoint : Entity<Checkpoint>
{
    public static string Key(string participantId, Scope scope) => participantId + "/" + scope;

    [Index(Name = "ix_checkpoint_participants")]
    public string ParticipantId { get; set; } = "";

    public Scope Scope { get; set; } = Scope.Place;

    public long Seq { get; set; }

    public static Checkpoint At(string participantId, Scope scope, long seq) => new()
    {
        Id = Key(participantId, scope),
        ParticipantId = participantId,
        Scope = scope,
        Seq = seq
    };
}
