using Koan.Data.Core;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Writer;

namespace Tangent.Events;

/// <summary>Moves a participant's notifications checkpoint to the newest notification a page of them showed, so the
/// count every page shows reads past it. Never backwards: a sequence at or behind the checkpoint answers with the
/// checkpoint as it stands and writes nothing. The participant exists and the place exists for them, or the command
/// answers as for a missing place; the sequence names an event of the log that targets them, or the command is refused.
/// Seeing a notification decides nothing: an Offer among them stays open until its decider answers it.</summary>
public sealed record SeeNotifications(string ParticipantId, long Through) : ICommand<Checkpoint>
{
    public const string NoPlace = CatchUp.NoPlace;
    public const string NoNotification = "No notification of yours has this sequence.";

    public async Task<Outcome<Checkpoint>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Participant.Get(ParticipantId, ct) is null) return new Outcome<Checkpoint>.Refused(NoPlace);
        if (!(await Capabilities.Load(ParticipantId, null, null, ct)).Exists) return new Outcome<Checkpoint>.Refused(NoPlace);
        if (Through < 1 || await Event.Get(Event.Key(Through), ct) is not { } seen || seen.TargetId != ParticipantId)
            return new Outcome<Checkpoint>.Refused(NoNotification);

        var existing = await Checkpoint.Get(Checkpoint.Key(ParticipantId, Scope.Notifications), ct);
        if (existing is not null && existing.Seq >= Through) return new Outcome<Checkpoint>.Done(existing);

        var moved = Checkpoint.At(ParticipantId, Scope.Notifications, Through);
        await moved.Save(ct);
        return new Outcome<Checkpoint>.Done(moved);
    }
}
