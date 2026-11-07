using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Querying;

namespace Tangent.Tangents;

/// <summary>Counts a Tangent's Topics again in their months and by their authors (<see cref="Tangent.Retally"/>), in one
/// hold, where those do not add up to the Topics started there (<see cref="Tangent.Tallied"/>): its Topics are read a page
/// at a time (<see cref="TopicList"/>), and holds take turns, so no Topic starts while they are. A Tangent that adds up is
/// answered as it is. Every start sends it for each Tangent that does not (<see cref="AtStart"/>), as a folder restored
/// from a copy may hold.</summary>
public sealed record TallyTopics(string Slug) : ICommand<Tangent>
{
    /// <summary>The most Tangents one read of the start takes.</summary>
    public const int TangentPage = 100;

    public async Task<Outcome<Tangent>> Run(CancellationToken ct)
    {
        if (await Tangent.Get(Slug, ct) is not { } tangent) return new Outcome<Tangent>.Refused(ChangeTangent.NoTangent);
        if (tangent.Tallied()) return new Outcome<Tangent>.Done(tangent);

        var roots = new List<Posts.Post>();
        KeysetKey? cursor = null;
        do
        {
            var page = await TopicList.Read(Slug, cursor, ct);
            roots.AddRange(page.Topics);
            cursor = page.Next;
        } while (cursor is not null);

        tangent.Retally(roots);
        await tangent.Save(ct);
        return new Outcome<Tangent>.Done(tangent);
    }

    /// <summary>Tallies every Tangent whose months and authors do not add up to its Topics, reading the Tangents a page of
    /// <see cref="TangentPage"/> at a time, and says each it tallied.</summary>
    public static async Task AtStart(ILogger logger, CancellationToken ct)
    {
        (int Order, string Id)? after = null;
        while (true)
        {
            var tangents = await Tangent.InOrder(after, false, null, TangentPage, ct);
            foreach (var tangent in tangents.Where(tangent => !tangent.Tallied()))
                switch (await new TallyTopics(tangent.Id).Send(ct))
                {
                    case Outcome<Tangent>.Done { Result: var tallied }:
                        logger.LogInformation("The months and authors of {Tangent} did not add up to its {Topics} Topics; this start counted them again",
                            tallied.Id, tallied.TopicCount);
                        break;
                    case var other:
                        logger.LogError("The months and authors of {Tangent} do not add up to its Topics, and this start could not count them again: {Outcome}",
                            tangent.Id, other);
                        break;
                }

            if (tangents.Count < TangentPage) return;
            after = (tangents[^1].Order, tangents[^1].Id);
        }
    }
}
