using TangentSpace.Conversation;

internal static class ProbeChecks
{
    internal static double NearestRank(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0 || percentile is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(percentile));
        return sorted[(int)Math.Ceiling(percentile * sorted.Count) - 1];
    }

    internal static void ValidateWindow(IReadOnlyList<Post> rows, long edge, bool descending)
    {
        if (rows.Count != 21 || rows.Select(row => row.Id).Distinct().Count() != 21)
            throw new InvalidDataException("Window row count or identity uniqueness failed.");
        for (var i = 0; i < rows.Count; i++)
        {
            var sequence = descending ? edge - 1 - i : edge + 1 + i;
            if (rows[i].RoomKey != "epic005-hot-topic" || rows[i].Sequence != sequence || rows[i].Id != "epic005-" + sequence.ToString("D10"))
                throw new InvalidDataException("Window has a gap, wrong scope, wrong identity or incorrect order.");
        }
    }

    internal static void SelfTest()
    {
        if (NearestRank([1, 2, 3, 4, 5, 6], .5) != 3 || NearestRank([1, 2, 3, 4, 5, 6], .95) != 6)
            throw new Exception("Nearest-rank percentile self-test failed.");
        var rows = Enumerable.Range(1, 21).Select(sequence => new Post { Id = "epic005-" + sequence.ToString("D10"), RoomKey = "epic005-hot-topic", Sequence = sequence }).ToArray();
        ValidateWindow(rows, 0, false);
        ValidateWindow(rows.Reverse().ToArray(), 22, true);
        rows[20].Sequence = 1;
        try { ValidateWindow(rows, 0, false); }
        catch (InvalidDataException) { return; }
        throw new Exception("Corrupt-window rejection self-test failed.");
    }
}
