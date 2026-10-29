using Koan.Data.Core;

namespace Tangent.Writer;

/// <summary>Creates the slip the sender names, numbered from the writer's mark. Idempotent by Id: the same Id with
/// the same body answers with the existing slip; a different body is a conflict.</summary>
public sealed record CreateSlip(string Id, string Sender, int Ordinal, string Body) : ICommand<Slip>
{
    /// <summary>The high-water mark slips are numbered from.</summary>
    public const string Mark = "slip";

    /// <summary>The mark, loaded by the one bounded query: the highest sequence stored, or zero.</summary>
    public static readonly HighWaterMark HighWaterMark = new(Mark, async ct =>
        (await Slip.FirstPage(1, sort => sort.OrderByDescending(slip => slip.Sequence), ct)).FirstOrDefault()?.Sequence ?? 0);

    public async Task<Outcome<Slip>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Slip.Get(Id, ct) is { } existing)
        {
            return existing.Body == Body
                ? new Outcome<Slip>.Done(existing)
                : new Outcome<Slip>.Conflict($"Slip '{Id}' already exists with a different body.");
        }

        // The sequence is taken before the entity can refuse, so a refusal proves the writer gives it back.
        var slip = Slip.Write(Id, Sender, Ordinal, Body, sequences.Next(Mark));
        await slip.Save(ct);
        return new Outcome<Slip>.Done(slip);
    }
}

/// <summary>Saves a slip and a stub under one Id, and throws between the two saves when asked to.</summary>
public sealed record WriteSlipAndStub(string Id, bool ThrowBetween) : ICommand<string>
{
    public const string ThrownBetween = "thrown between the two saves";

    public async Task<Outcome<string>> Run(Sequences sequences, CancellationToken ct)
    {
        await new Slip { Id = Id, Sender = "pair", Body = "slip", Sequence = sequences.Next(CreateSlip.Mark) }.Save(ct);
        if (ThrowBetween) throw new InvalidOperationException(ThrownBetween);
        await new Stub { Id = Id, Note = "stub" }.Save(ct);
        return new Outcome<string>.Done(Id);
    }
}

/// <summary>Saves a slip, then holds the loop until the test releases it, so the test can look for the row from
/// outside while the command's scope is open. Records whether the command itself could see its row before commit.</summary>
public sealed class HoldSlip(string id) : ICommand<Slip>
{
    public TaskCompletionSource Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool VisibleToItselfBeforeCommit { get; private set; }

    public async Task<Outcome<Slip>> Run(Sequences sequences, CancellationToken ct)
    {
        var slip = new Slip { Id = id, Sender = "held", Body = "held", Sequence = sequences.Next(CreateSlip.Mark) };
        await slip.Save(ct);
        VisibleToItselfBeforeCommit = await Slip.Get(id, ct) is not null;
        Saved.SetResult();
        await Release.Task.WaitAsync(ct);
        return new Outcome<Slip>.Done(slip);
    }
}

/// <summary>Holds the loop until the writer is stopped, and says when it started running.</summary>
public sealed class WaitForStop : ICommand<bool>
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<Outcome<bool>> Run(Sequences sequences, CancellationToken ct)
    {
        Started.SetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        return new Outcome<bool>.Done(true);
    }
}
