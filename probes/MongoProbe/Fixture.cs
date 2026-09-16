using TangentSpace.Conversation;

internal static class Fixture
{
    internal static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal static string Id(string room, long sequence) => "mongo-" + room + "-" + sequence.ToString("D10");
    internal static string Text(long sequence) => sequence % 97 == 0 ? new string('x', 3900) : "Synthetic @member #scale " + sequence + " — café 日本語";
    internal static Post Make(string room, long sequence) => new()
    {
        Id = Id(room, sequence), RoomKey = room, Sequence = sequence,
        AuthorParticipantId = "synthetic-" + (sequence % 32).ToString("D2"),
        SourceUri = "at://synthetic.invalid/local.tangent.message/" + sequence, SourceCid = "synthetic-cid-" + sequence,
        AcceptedAt = Epoch.AddSeconds(sequence), Content = new(Text(sequence), Epoch.AddSeconds(sequence), null),
        Removed = sequence % 101 == 0, EditedAt = sequence % 53 == 0 ? Epoch.AddDays(2) : null, Facets = []
    };
    internal static void Validate(IReadOnlyList<Post> rows, string room, long edge, bool descending)
    {
        if (rows.Count != 21 || rows.Select(row => row.Id).Distinct().Count() != 21)
            throw new InvalidDataException("Wrong row count or duplicate identities.");
        for (var i = 0; i < rows.Count; i++)
        {
            var sequence = descending ? edge - i - 1 : edge + i + 1;
            if (rows[i].Id != Id(room, sequence) || rows[i].Sequence != sequence || rows[i].RoomKey != room
                || rows[i].Content.Text != Text(sequence) || rows[i].Removed != (sequence % 101 == 0)
                || rows[i].EditedAt.HasValue != (sequence % 53 == 0))
                throw new InvalidDataException("Window order/scope/identity/content/tombstone/edit mismatch.");
        }
    }
    internal static double Rank(IReadOnlyList<double> sorted, double p) => sorted[(int)Math.Ceiling(p * sorted.Count) - 1];
    internal static void SelfTest()
    {
        if (Rank([1, 2, 3, 4, 5, 6], .5) != 3 || Rank([1, 2, 3, 4, 5, 6], .95) != 6) throw new Exception("Percentile self-test failed.");
        var rows = Enumerable.Range(80, 21).Select(n => Make("hot", n)).ToArray();
        Validate(rows, "hot", 79, false); Validate(rows.Reverse().ToArray(), "hot", 101, true);
        rows[^1].RoomKey = "wrong";
        try { Validate(rows, "hot", 79, false); } catch (InvalidDataException) { return; }
        throw new Exception("Window corruption was not rejected.");
    }
}
