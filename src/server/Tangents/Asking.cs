using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;

namespace Tangent.Tangents;

/// <summary>What the door says of asks to join: to whoever manages a members-only Tangent, how many wait there
/// (<see cref="For"/>), and to the asker, who decided (<see cref="Decided"/>).</summary>
public static class Asking
{
    /// <summary>The asks that wait in each of <paramref name="tangents"/> that the reader manages, as <paramref name="each"/>
    /// says what they hold there, in the Tangents' order, where asks wait there (<see cref="Inside.AsksWait"/>): for each whose
    /// joining is not join, its audience and one read of at most <see cref="Ask.ReadSize"/> and one more of its waiting asks. A
    /// count is a lower bound where its read was full.</summary>
    public static async Task<IReadOnlyList<Door.Asking>> For(IReadOnlyList<Tangent> tangents, IReadOnlyDictionary<string, Capabilities> each,
        string baseAddress, CancellationToken ct)
    {
        var asking = new List<Door.Asking>();
        foreach (var row in tangents)
        {
            if (row.Joining == Joining.Join || !each.TryGetValue(row.Id, out var held) || !held.Holds(Bit.ManageTangent)) continue;
            if (!Inside.AsksWait(row, await Overwrites.AudienceOf(row.Id, ct))) continue;
            var waiting = await Ask.Waiting(row.Id, Ask.ReadSize + 1, ct);
            if (waiting.Count > 0)
                asking.Add(new Door.Asking(row.Name, Math.Min(waiting.Count, Ask.ReadSize), waiting.Count > Ask.ReadSize,
                    TangentPeopleController.PeopleAddress(baseAddress, row.Id)));
        }

        return asking;
    }

    /// <summary>The decisions about the reader's asks, from their rows of the log, with one keyed read of the Tangents
    /// they name.</summary>
    public static async Task<IReadOnlyList<Door.Let>?> Decided(IReadOnlyList<Event> rows, IReadOnlyDictionary<string, Render.Person> people,
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
