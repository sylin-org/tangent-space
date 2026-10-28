namespace Tangent.Activity;

internal sealed record ActivityPagePlan(IReadOnlyList<long> DeliveredSequences, long After, bool HasMore);

internal static class ActivityPaging
{
    // A consumer can always advance across an inaccessible marker, but never across a visible marker it has not received.
    public static ActivityPagePlan Plan(long initialAfter, long boundary, IReadOnlyList<(long Sequence, bool Visible)> entries,
        bool moreEntries, int maximumDelivered)
    {
        var delivered = new List<long>(maximumDelivered);
        var after = initialAfter;
        foreach (var entry in entries)
        {
            if (entry.Visible)
            {
                if (delivered.Count == maximumDelivered) return new(delivered, after, true);
                delivered.Add(entry.Sequence);
            }
            after = entry.Sequence;
        }
        return new(delivered, moreEntries ? after : boundary, moreEntries);
    }

    public static string Continue(string current, ActivityCursor previous, ActivityCursor next, Func<ActivityCursor, string> encode)
        => next == previous ? current : encode(next);
}
