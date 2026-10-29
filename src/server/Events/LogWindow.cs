using Koan.Data.Abstractions;
using Koan.Data.Core.Sorting;

namespace Tangent.Events;

/// <summary>The log as the door reads it: two bounded reads, newest first, each one index range with a limit. The
/// newest <see cref="Size"/> events through <c>ix_event_seq</c>, which holds every event past a reader's place
/// checkpoint when fewer than <see cref="Size"/> came after it, and the newest <see cref="Size"/> of them otherwise —
/// what reading <c>Seq</c> past the checkpoint, newest first, would answer — and, beside the front page, what is
/// alive. And the events that name one participant as their target past a sequence, through <c>ix_event_targets</c>
/// (<c>TargetId</c>, <c>Seq</c>), at most <see cref="TargetedSize"/>: replies to them and mentions of them, which a
/// busy log would carry out of the first window. Reads only and takes no lock; who may see an event is the caller's to
/// decide.</summary>
public static class LogWindow
{
    /// <summary>The most events the door reads from the head of the log.</summary>
    public const int Size = 200;

    /// <summary>The most events naming one participant as their target the door reads.</summary>
    public const int TargetedSize = 50;

    /// <summary>The newest events of the log, newest first, at most <see cref="Size"/>.</summary>
    public static Task<IReadOnlyList<Event>> Newest(CancellationToken ct)
        => Event.FirstPage(Size, sort => sort.OrderByDescending(e => e.Seq), ct);

    /// <summary>The events past <paramref name="since"/> whose target is <paramref name="participantId"/>, newest first,
    /// at most <see cref="TargetedSize"/>.</summary>
    public static Task<IReadOnlyList<Event>> Targeting(string participantId, long since, CancellationToken ct)
        => Event.Query(e => e.TargetId == participantId && e.Seq > since,
            new QueryDefinition { Page = 1, PageSize = TargetedSize }.WithSort<Event>(sort => sort.OrderByDescending(e => e.Seq)), ct);

    /// <summary>Whether the <see cref="Newest"/> window may have left out events past <paramref name="since"/>: it is
    /// full and its oldest event is not the one right after <paramref name="since"/>. The log's sequence has no gap, so
    /// a window that reaches that event holds everything after it.</summary>
    public static bool Partial(IReadOnlyList<Event> window, long since)
        => window.Count >= Size && window[^1].Seq > since + 1;
}
