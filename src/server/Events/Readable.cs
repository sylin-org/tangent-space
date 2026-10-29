using Koan.Data.Abstractions;
using Koan.Data.Core.Sorting;
using Tangent.Access;

namespace Tangent.Events;

/// <summary>Which rows of the log reach one reader, by the rule the door reads by: a row in a Tangent reaches them where
/// they may view that Tangent, and is absent otherwise; a row in no Tangent reaches them where it changed the place, or
/// where it names them as its target. A poll asks once; a stream holds the answers for each Tangent it has met and
/// asks again for all of them when <see cref="Refresh"/> has passed since it last asked, or at once when a row it is
/// about to send changed a standing, a role, a Tangent or the place, so a reader who loses <see cref="Bit.View"/>
/// receives nothing more from that Tangent from the first row past the change that the writer commits more than
/// <see cref="Refresh"/> after it, and from the very next row where the change is itself in the log. Each ask is the bounded,
/// lock-free read capability makes (<see cref="Capabilities.LoadEach"/>), for the Tangents the rows name and nothing
/// else.</summary>
public sealed class Readable(string? participantId)
{
    /// <summary>How long a stream holds what it read of a reader's capabilities before it reads them again.</summary>
    public static readonly TimeSpan Refresh = TimeSpan.FromSeconds(1);

    /// <summary>The most rows one read of the log for a reader takes: a poll's page, a stream's catch-up, where a reader
    /// begins.</summary>
    public const int ScanLimit = 200;

    private readonly Dictionary<string, bool> _tangents = new(StringComparer.Ordinal);
    private DateTimeOffset _read = DateTimeOffset.MinValue;
    private bool _place = true;
    private bool _placeRead;

    /// <summary>Whether the reader may still read the place: false once a refresh found the place absent to them, when
    /// their stream ends.</summary>
    public bool Place => _place;

    /// <summary>Reads what the reader holds in each Tangent <paramref name="rows"/> name that it does not hold already,
    /// first forgetting everything when it is stale or a row changed what anyone may read.</summary>
    public async Task Prepare(IReadOnlyList<Event> rows, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _read >= Refresh || rows.Any(Changes))
        {
            _tangents.Clear();
            _placeRead = false;
            _read = now;
        }

        if (!_placeRead && rows.Any(row => row.TangentId is null))
        {
            _place = (await Capabilities.Load(participantId, null, null, ct)).Exists;
            _placeRead = true;
        }

        var missing = rows.Select(row => row.TangentId).OfType<string>().Distinct(StringComparer.Ordinal)
            .Where(id => !_tangents.ContainsKey(id)).ToList();
        if (missing.Count == 0) return;
        foreach (var (id, capabilities) in await Capabilities.LoadEach(participantId, missing, ct)) _tangents[id] = capabilities.Exists;
    }

    /// <summary>Whether <paramref name="row"/> reaches the reader; <see cref="Prepare"/> has read what it needs.</summary>
    public bool Reaches(Event row) => row.TangentId is { } tangent
        ? _tangents.GetValueOrDefault(tangent)
        : _place && (row.Kind == EventKind.SpaceChanged || (participantId is not null && row.TargetId == participantId));

    /// <summary>Where a reader begins on the log at <paramref name="head"/>: the newest row through it that reaches them,
    /// among the newest <see cref="ScanLimit"/>, or <paramref name="head"/> itself only when none of those reaches them,
    /// so the position says nothing of hidden rows but that a whole scan of them passed. One bounded read of the log's
    /// sequence index, and what capability reads for the Tangents it names.</summary>
    public static async Task<long> Newest(string? participantId, long head, CancellationToken ct)
    {
        var rows = await Event.Query(e => e.Seq <= head,
            new QueryDefinition { Page = 1, PageSize = ScanLimit }.WithSort<Event>(sort => sort.OrderByDescending(e => e.Seq)), ct);
        if (rows.Count == 0) return 0;
        var readable = new Readable(participantId);
        await readable.Prepare(rows, ct);
        return rows.FirstOrDefault(readable.Reaches)?.Seq ?? head;
    }

    /// <summary>Whether a row changes what someone may read.</summary>
    private static bool Changes(Event row)
        => row.Kind is EventKind.StandingChanged or EventKind.RoleChanged or EventKind.TangentChanged or EventKind.SpaceChanged;
}
