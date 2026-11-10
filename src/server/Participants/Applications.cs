using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Sorting;
using Tangent.Access;
using Tangent.Events;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>The applications of a place whose admission says apply: the participants who applied, waiting or declined.
/// <see cref="Decide"/> is what <see cref="Admit"/> and <see cref="Decline"/> share; <see cref="Among"/> is which of them a
/// list reads, which <see cref="ApplicationsController"/> pages newest application first, a page of <see cref="PageSize"/>.
/// <see cref="Waiting"/> reads those that wait, by where they arrived in the log, through <c>ix_participant_waiting</c>
/// (<see cref="Participant.WaitingSeq"/>), which is how they reach whoever decides them; nothing is counted past what one
/// bounded read holds.</summary>
public static class Applications
{
    /// <summary>The most applications one page holds.</summary>
    public const int PageSize = 25;

    /// <summary>The answer to a decider for whom the place does not exist.</summary>
    public static readonly Reason NoPlace = Reason.Absent("No Tangent Space is here.");

    /// <summary>The answer to a decision about someone who never arrived.</summary>
    public static readonly Reason NoApplicant = Reason.Absent("No one by that id has arrived here.");

    /// <summary>One decision: the place exists for the actor, who holds <see cref="Bit.View"/> on it, or the command
    /// answers that no place is here; the actor holds <see cref="Bit.ManageStanding"/>, or is refused with the gate word;
    /// the participant has arrived, or the command answers that no one did; <paramref name="decide"/> changes their
    /// standing, or refuses with <see cref="Participant.NotAnApplication"/>. A decision that changes something appends
    /// one <see cref="EventKind.StandingChanged"/> with the actor, the applicant as its target, the standing it left
    /// them with and the one they held before; one that changes nothing writes nothing.</summary>
    public static async Task<Outcome<Participant>> Decide(string actorId, string participantId,
        Func<Participant, DateTimeOffset, bool> decide, DateTimeOffset? at, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is null) return new Outcome<Participant>.Refused(NoPlace);
        var capabilities = await Capabilities.Load(actorId, null, null, ct);
        if (!capabilities.Exists) return new Outcome<Participant>.Refused(NoPlace);
        if (capabilities.Gate(Bit.ManageStanding) is { } gate) return new Outcome<Participant>.Refused(gate);
        if (string.IsNullOrWhiteSpace(participantId) || await Participant.Get(participantId, ct) is not { } participant)
            return new Outcome<Participant>.Refused(NoApplicant);

        var now = at ?? DateTimeOffset.UtcNow;
        var before = participant.StandingAt(now);
        if (!decide(participant, now)) return new Outcome<Participant>.Done(participant);
        await participant.Save(ct);
        await Event.Append(EventKind.StandingChanged, now, ct, actorId: actorId, targetId: participant.Id,
            standing: participant.Standing, before: before);
        return new Outcome<Participant>.Done(participant);
    }

    /// <summary>The applications a list reads: those that wait alone where <paramref name="waiting"/>, otherwise every one,
    /// waiting or declined.</summary>
    public static Expression<Func<Participant, bool>> Among(bool waiting)
        => waiting
            ? participant => participant.Standing == Standing.Applied
            : participant => participant.Standing == Standing.Applied || participant.Standing == Standing.Declined;

    /// <summary>At most <paramref name="limit"/> of the applications that wait, newest first: those whose arrival lies
    /// after the log's sequence <paramref name="after"/> and before <paramref name="before"/>, one range of
    /// <c>ix_participant_waiting</c>. An application admitted or declined no longer waits, so a decision by anyone ends it
    /// for everyone.</summary>
    public static Task<IReadOnlyList<Participant>> Waiting(long after, long before, int limit, CancellationToken ct)
        => Participant.Query(participant => participant.WaitingSeq > after && participant.WaitingSeq < before,
            new QueryDefinition { Page = 1, PageSize = limit }
                .WithSort<Participant>(sort => sort.OrderByDescending(participant => participant.WaitingSeq)), ct);
}
