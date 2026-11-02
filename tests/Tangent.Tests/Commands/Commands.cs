using Koan.Data.Core;
using Koan.Data.Core.Commands;

namespace Tangent.Commands;

/// <summary>Creates the slip the sender names, numbered from <see cref="Numbers"/>. Idempotent by Id: the same Id with
/// the same body answers with the existing slip; a different body is a conflict.</summary>
public sealed record CreateSlip(string Id, string Sender, int Ordinal, string Body) : ICommand<Slip>
{
    /// <summary>The slips' numbers.</summary>
    public static readonly Sequence Numbers = Sequence.Of<Slip>(slip => slip.Sequence);

    public async Task<Outcome<Slip>> Run(CancellationToken ct)
    {
        if (await Slip.Get(Id, ct) is { } existing)
        {
            return existing.Body == Body
                ? new Outcome<Slip>.Done(existing)
                : new Outcome<Slip>.Refused(Reason.Conflict($"Slip '{Id}' already exists with a different body."));
        }

        // The number is taken before the entity can refuse, so a refusal proves it goes back.
        var slip = Slip.Write(Id, Sender, Ordinal, Body, await Numbers.Next(ct));
        await slip.Save(ct);
        return new Outcome<Slip>.Done(slip);
    }
}

/// <summary>Saves a slip and a stub under one Id, and throws between the two saves when asked to.</summary>
public sealed record WriteSlipAndStub(string Id, bool ThrowBetween) : ICommand<string>
{
    public const string ThrownBetween = "thrown between the two saves";

    public async Task<Outcome<string>> Run(CancellationToken ct)
    {
        await new Slip { Id = Id, Sender = "pair", Body = "slip", Sequence = await CreateSlip.Numbers.Next(ct) }.Save(ct);
        if (ThrowBetween) throw new InvalidOperationException(ThrownBetween);
        await new Stub { Id = Id, Note = "stub" }.Save(ct);
        return new Outcome<string>.Done(Id);
    }
}

/// <summary>Saves a slip, then waits until the test releases it, so the test can look for the row from outside while the
/// command's hold is open. Records whether the command itself sees its row before commit.</summary>
public sealed class HoldSlip(string id) : ICommand<Slip>
{
    public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool VisibleToItselfBeforeCommit { get; private set; }

    public async Task<Outcome<Slip>> Run(CancellationToken ct)
    {
        var slip = new Slip { Id = id, Sender = "held", Body = "held", Sequence = await CreateSlip.Numbers.Next(ct) };
        await slip.Save(ct);
        VisibleToItselfBeforeCommit = await Slip.Get(id, ct) is not null;
        Saved.SetResult();
        await Release.Task.WaitAsync(ct);
        return new Outcome<Slip>.Done(slip);
    }
}
