using Koan.Data.Core;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Writer;

namespace Tangent.Events;

/// <summary>Moves a participant's place checkpoint to the sequence they read the door through, so the door shows
/// nothing new until something happens. Never backwards: a sequence at or behind the checkpoint answers with the
/// checkpoint as it stands and writes nothing. The participant exists and the place exists for them, or the command
/// answers as for a missing place; the sequence names an event of the log, or the command is refused. The watch flag
/// stays as it was.</summary>
public sealed record CatchUp(string ParticipantId, long Through) : ICommand<Checkpoint>
{
    public const string NoPlace = "No Tangent Space is here.";
    public const string NoEvent = "No event has this sequence.";

    public async Task<Outcome<Checkpoint>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Participant.Get(ParticipantId, ct) is null) return new Outcome<Checkpoint>.Refused(NoPlace);
        if (!(await Capabilities.Load(ParticipantId, null, null, ct)).Exists) return new Outcome<Checkpoint>.Refused(NoPlace);
        if (Through < 1 || await Event.Get(Event.Key(Through), ct) is null) return new Outcome<Checkpoint>.Refused(NoEvent);

        var existing = await Checkpoint.Get(Checkpoint.Key(ParticipantId, Scope.Place), ct);
        if (existing is not null && existing.Seq >= Through) return new Outcome<Checkpoint>.Done(existing);

        var moved = Checkpoint.At(ParticipantId, Scope.Place, Through, existing?.Watch ?? false);
        await moved.Save(ct);
        return new Outcome<Checkpoint>.Done(moved);
    }
}
