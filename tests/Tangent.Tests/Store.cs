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
