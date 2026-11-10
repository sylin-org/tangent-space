using System.Linq.Expressions;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Sorting;

namespace Tangent.Participants;

/// <summary>The applications of a place whose admission says apply: the participants who applied, waiting or declined.
/// An applicant is admitted or declined as any standing is given (<see cref="SetStanding"/>). <see cref="Among"/> is which of them a
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
