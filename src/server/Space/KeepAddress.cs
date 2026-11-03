using Koan.Data.Core;
using Koan.Data.Core.Commands;

namespace Tangent.Space;

/// <summary>Keeps <see cref="Address"/>, an origin Koan has validated, as the place's address on its row: what
/// <see cref="PlaceAddress"/> writes when Koan accepts a URL. Idempotent: the address the row already holds answers
/// with the row and nothing is written. A place not set up is refused with <see cref="NoPlace"/>.</summary>
public sealed record KeepAddress(string Address) : ICommand<TangentSpace>
{
    public static readonly Reason NoPlace = Reason.Invalid("This Tangent Space is not set up yet, so it has no address to keep.");

    public async Task<Outcome<TangentSpace>> Run(CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return new Outcome<TangentSpace>.Refused(NoPlace);
        if (space.TakeAddress(Address)) await space.Save(ct);
        return new Outcome<TangentSpace>.Done(space);
    }
}
