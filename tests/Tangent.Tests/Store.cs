using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;

namespace Tangent;

/// <summary>Saves one row as the test built it, through the so a proof that needs a row in a given state
/// gets it the way every row is written.</summary>
public sealed record Store<TEntity>(TEntity Row) : ICommand<TEntity> where TEntity : Entity<TEntity>
{
    public async Task<Outcome<TEntity>> Run(CancellationToken ct)
    {
        await Row.Save(ct);
        return new Outcome<TEntity>.Done(Row);
    }
}

/// <summary>A Tangent changed as changing one writes it (<see cref="Tangents.TangentWriting.Changed"/>): <see cref="Change"/>
/// on its row, its <see cref="Audience"/> and its <see cref="Move"/>.</summary>
public sealed record ChangeTangentRow(string Slug, string ActorId, Action<Tangents.Tangent>? Change = null, Space.Audience? Audience = null,
    Tangents.Move? Move = null) : ICommand<Tangents.Tangent>
{
    public async Task<Outcome<Tangents.Tangent>> Run(CancellationToken ct)
    {
        var prior = (await Tangents.Tangent.Get(Slug, ct))!;
        var working = (await Tangents.Tangent.Get(Slug, ct))!;
        Change?.Invoke(working);
        if (await Tangents.TangentWriting.Changed(prior, working, Audience, Move, ActorId, DateTimeOffset.UtcNow, ct)) await working.Save(ct);
        return new Outcome<Tangents.Tangent>.Done(working);
    }
}

/// <summary>A Tangent deleted as deleting one does (<see cref="Tangents.TangentWriting.Deleting"/>), with the
/// <see cref="Topics"/> it holds.</summary>
public sealed record DeleteTangentRow(string Slug, string ActorId, int Topics = 0) : ICommand<Tangents.Tangent>
{
    public async Task<Outcome<Tangents.Tangent>> Run(CancellationToken ct)
    {
        var tangent = (await Tangents.Tangent.Get(Slug, ct))!;
        await Tangents.TangentWriting.Deleting(tangent, Topics, ActorId, DateTimeOffset.UtcNow, ct);
        await tangent.Remove(ct);
        return new Outcome<Tangents.Tangent>.Done(tangent);
    }
}
