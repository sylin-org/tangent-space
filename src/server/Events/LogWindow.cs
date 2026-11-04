using Koan.Data.Core.Sorting;

namespace Tangent.Events;

/// <summary>The log as the door reads it beside what is for its reader (<see cref="Unread.ForYouNow"/>): the newest
/// <see cref="Size"/> events, one bounded read through <c>ix_event_seq</c>, newest first, which holds every event past a
/// reader's floor when fewer than <see cref="Size"/> came after it, and the newest <see cref="Size"/> of them otherwise,
/// and, beside the front page, what is alive. Reads only and takes no lock; who may see an event is the caller's to
/// decide.</summary>
public static class LogWindow
{
    /// <summary>The most events the door reads from the head of the log.</summary>
    public const int Size = 200;

    /// <summary>The newest events of the log, newest first, at most <see cref="Size"/>.</summary>
    public static Task<IReadOnlyList<Event>> Newest(CancellationToken ct)
        => Event.FirstPage(Size, sort => sort.OrderByDescending(e => e.Seq), ct);

    /// <summary>Whether the <see cref="Newest"/> window may have left out events past <paramref name="since"/>: it is
    /// full and its oldest event is not the one right after <paramref name="since"/>. The log's sequence has no gap, so
    /// a window that reaches that event holds everything after it.</summary>
    public static bool Partial(IReadOnlyList<Event> window, long since)
        => window.Count >= Size && window[^1].Seq > since + 1;
}
