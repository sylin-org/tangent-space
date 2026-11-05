using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>What the door says of asks to join: to whoever manages a members-only Tangent, how many wait there
/// (<see cref="For"/>), and to the asker, who decided (<see cref="Decided"/>).</summary>
public static class Asking
{
    /// <summary>The asks that wait in the Tangents <paramref name="personId"/> manages, by Tangent, newest first, where asks
    /// wait there (<see cref="Inside.AsksWait"/>): one read of at most <see cref="Ask.ReadSize"/> and one more waiting asks,
    /// what capability reads for the Tangents they name, one keyed read of those Tangents, and the audience of each that
    /// still asks. A count is a lower bound where the read was full.</summary>
    public static async Task<IReadOnlyList<Door.Asking>> For(string personId, string baseAddress, CancellationToken ct)
    {
        var waiting = await Ask.Waiting(null, Ask.ReadSize + 1, ct);
        if (waiting.Count == 0) return [];
        var full = waiting.Count > Ask.ReadSize;
        var ids = waiting.Select(ask => ask.TangentId).Distinct(StringComparer.Ordinal).ToList();
        var each = await Capabilities.LoadEach(personId, ids, ct);
        var managed = ids.Where(id => each[id].Holds(Bit.ManageTangent)).ToList();
        if (managed.Count == 0) return [];
        var rows = (await Tangent.Get(managed, ct)).OfType<Tangent>().ToDictionary(row => row.Id, StringComparer.Ordinal);
        var asking = new List<Door.Asking>();
        foreach (var id in managed.Where(id => rows.TryGetValue(id, out var row) && row.Joining != Joining.Join))
            if (Inside.AsksWait(rows[id], await Overwrites.AudienceOf(id, ct)))
                asking.Add(new Door.Asking(rows[id].Name, waiting.Count(ask => ask.TangentId == id), full, TangentPeopleController.PeopleAddress(baseAddress, id)));
        return asking;
    }

    /// <summary>The decisions about the reader's asks, from their rows of the log, with one keyed read of the Tangents
    /// they name.</summary>
    public static async Task<IReadOnlyList<Door.Let>?> Decided(IReadOnlyList<Event> rows, IReadOnlyDictionary<string, TangentPage.Author> people,
        string baseAddress, CancellationToken ct)
    {
        if (rows.Count == 0) return null;
        var ids = rows.Select(row => row.TangentId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var named = (await Tangent.Get(ids, ct)).OfType<Tangent>().ToDictionary(row => row.Id, StringComparer.Ordinal);
        return rows.Where(row => row.TangentId is { } id && named.ContainsKey(id))
            .Select(row => new Door.Let(TopicPageController.Person(people, row.ActorId!), row.At, named[row.TangentId!].Name,
                PostWrites.TangentAddress(baseAddress, row.TangentId!), row.Given!.Value))
            .ToList();
    }
}
