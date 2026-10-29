using Koan.Data.Core;
using Koan.Data.Core.Model;
using Tangent.Writer;

namespace Tangent;

/// <summary>Saves one row as the test built it, through the writer, so a proof that needs a row in a given state
/// gets it the way every row is written.</summary>
public sealed record Store<TEntity>(TEntity Row) : ICommand<TEntity> where TEntity : Entity<TEntity>
{
    public async Task<Outcome<TEntity>> Run(Sequences sequences, CancellationToken ct)
    {
        await Row.Save(ct);
        return new Outcome<TEntity>.Done(Row);
    }
}
