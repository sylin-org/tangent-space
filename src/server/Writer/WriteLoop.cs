using System.Threading.Channels;
using Koan.Core.BackgroundServices;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core;
using Koan.Data.Core.Transactions;

namespace Tangent.Writer;

/// <summary>The writer: one queue, one loop, one transaction per command. A sender hands a command to
/// <see cref="Send{TResult}"/> and awaits its outcome. The loop takes commands in arrival order and, for each, opens
/// one atomic scope, runs the command, assigns its sequences from the high-water marks loaded once at start, commits,
/// announces a commit that wrote to <see cref="Committed"/> and answers the sender. A command that is refused or throws
/// leaves no row behind and the next command runs. This is the only code in the tree that opens a transaction. Koan
/// discovers it as a background service and starts and stops it with the host.</summary>
public sealed class WriteLoop(ILogger<WriteLoop> logger, IConfiguration configuration, IEnumerable<HighWaterMark> marks)
    : KoanBackgroundServiceBase(logger, configuration)
{
    /// <summary>How many commands wait at most; a sender past this bound waits for a place in the queue.</summary>
    public const int QueueBound = 1024;

    /// <summary>The reason every sender receives whose command had not run when the loop ended.</summary>
    public const string Stopped = "The writer stopped before the command ran.";

    private readonly Channel<IPending> _queue = Channel.CreateBounded<IPending>(
        new BoundedChannelOptions(QueueBound) { SingleReader = true });
    private readonly IReadOnlyList<HighWaterMark> _marks = marks.ToList();
    private Exception? _ended;

    /// <summary>Raised in-process after each commit that wrote, on the loop, before the sender is answered. A
    /// subscriber returns at once; what it throws is logged and changes nothing.</summary>
    public event Action<Committed>? Committed;

    /// <summary>Hands a command to the loop and answers with its outcome once it has run. <paramref name="ct"/> bounds
    /// the sender's wait, not the command: a queued command runs unless the writer stops first, and then its sender
    /// receives the failure <see cref="Stopped"/>.</summary>
    public Task<Outcome<TResult>> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var pending = new Pending<TResult>(command, Logger);
        return _queue.Writer.TryWrite(pending) ? pending.Outcome.Task.WaitAsync(ct) : SendWhenFree(pending, ct);
    }

    private async Task<Outcome<TResult>> SendWhenFree<TResult>(Pending<TResult> pending, CancellationToken ct)
    {
        try
        {
            await _queue.Writer.WriteAsync(pending, ct);
        }
        catch (ChannelClosedException)
        {
            return new Outcome<TResult>.Failed(_ended ?? new OperationCanceledException(Stopped));
        }

        return await pending.Outcome.Task.WaitAsync(ct);
    }

    public override async Task ExecuteCore(CancellationToken ct)
    {
        var committed = new Dictionary<string, long>(StringComparer.Ordinal);
        Exception ended = new OperationCanceledException(Stopped);
        try
        {
            await LoadMarks(committed, ct);
            while (await _queue.Reader.WaitToReadAsync(ct))
            {
                // A stop is honoured at the command boundary: what is still queued never runs.
                while (!ct.IsCancellationRequested && _queue.Reader.TryRead(out var pending)) await Run(pending, committed, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            // A mark that could not be loaded, at start or after a commit of unknown outcome: the loop ends.
            ended = failure;
            throw;
        }
        finally
        {
            End(ended);
        }
    }

    /// <summary>Each mark from the bounded query its package supplies: once before the first command, and again
    /// after a commit whose outcome Koan could not report.</summary>
    private async Task LoadMarks(Dictionary<string, long> committed, CancellationToken ct)
    {
        var loaded = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var mark in _marks)
        {
            if (!loaded.TryAdd(mark.Name, await mark.Load(ct)))
                throw new InvalidOperationException($"Two high-water marks are named '{mark.Name}'.");
        }

        foreach (var (name, sequence) in loaded)
        {
            committed[name] = sequence;
            Logger.LogInformation("Writer high-water mark {Mark} at {Sequence}", name, sequence);
        }
    }

    /// <summary>One command: its own atomic scope, which never outlives it; a commit only for a result; the sender
    /// answered on every path out, after the scope is gone, so no sender continues inside it. A commit whose outcome
    /// is unknown may have landed, so its sequences are not given back: every mark is reloaded from the store before
    /// the next command runs.</summary>
    private async Task Run(IPending pending, Dictionary<string, long> committed, CancellationToken ct)
    {
        var name = pending.Command.GetType().Name;
        var sequences = new Sequences(committed);
        var wrote = false;
        var landed = false;
        var unknown = false;
        try
        {
            using (EntityContext.Transaction(name, TransactionGuarantee.Atomic))
            {
                if (await pending.Run(sequences, ct))
                {
                    wrote = (EntityContext.Current?.TransactionCoordinator?.TrackedOperationCount ?? 0) > 0;
                    try
                    {
                        // The commit itself is not cut short by a stop: a command that reached it either commits or
                        // fails on its own terms, bounded by the store's busy timeout.
                        await EntityContext.Commit(CancellationToken.None);
                        sequences.Keep();
                        landed = true;
                    }
                    catch (Exception failure)
                    {
                        unknown = !NothingLanded(failure);
                        Logger.LogError(failure, "Writer could not commit {Command}; outcome {Outcome}", name, unknown ? "unknown" : "nothing landed");
                        pending.Fail(failure);
                    }
                }
                else
                {
                    await EntityContext.Rollback(CancellationToken.None);
                }
            }
        }
        catch (Exception failure)
        {
            // Thrown before a commit was attempted: opening the scope, or discarding. Nothing landed.
            Logger.LogError(failure, "Writer could not run {Command}", name);
            pending.Fail(failure);
        }

        if (landed && wrote) Announce(new Committed(pending.Command, sequences.Taken));
        pending.Answer();
        if (unknown) await LoadMarks(committed, ct);
    }

    /// <summary>Whether Koan reports that a failed commit left nothing durable; anything else may have landed.</summary>
    private static bool NothingLanded(Exception failure) =>
        failure is TransactionException { CommitOutcome: DataCommitOutcome.NotDispatched or DataCommitOutcome.NotCommitted };

    private void Announce(Committed committed)
    {
        try
        {
            Committed?.Invoke(committed);
        }
        catch (Exception failure)
        {
            Logger.LogError(failure, "A subscriber of the writer's commits threw for {Command}", committed.Command.GetType().Name);
        }
    }

    /// <summary>Closes the queue and answers every command still in it: none of them ran.</summary>
    private void End(Exception cause)
    {
        _ended = cause;
        _queue.Writer.TryComplete();
        var unrun = 0;
        while (_queue.Reader.TryRead(out var pending))
        {
            pending.Fail(cause);
            pending.Answer();
            unrun++;
        }

        if (unrun > 0) Logger.LogInformation("Writer ended with {Count} commands that had not run", unrun);
    }

    private interface IPending
    {
        object Command { get; }

        /// <summary>Runs the command and holds its outcome; true when the outcome is a result the writer commits.</summary>
        Task<bool> Run(Sequences sequences, CancellationToken ct);

        void Fail(Exception cause);

        void Answer();
    }

    private sealed class Pending<TResult>(ICommand<TResult> command, ILogger logger) : IPending
    {
        private Outcome<TResult>? _outcome;

        public TaskCompletionSource<Outcome<TResult>> Outcome { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public object Command => command;

        public async Task<bool> Run(Sequences sequences, CancellationToken ct)
        {
            try
            {
                _outcome = await command.Run(sequences, ct)
                    ?? throw new InvalidOperationException($"{command.GetType().Name} returned no outcome.");
                return _outcome is Outcome<TResult>.Done;
            }
            catch (Refusal refusal)
            {
                _outcome = new Outcome<TResult>.Refused(refusal.Message);
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "Writer command {Command} failed", command.GetType().Name);
                _outcome = new Outcome<TResult>.Failed(failure);
            }

            return false;
        }

        public void Fail(Exception cause) => _outcome = new Outcome<TResult>.Failed(cause);

        public void Answer() => Outcome.TrySetResult(_outcome ?? new Outcome<TResult>.Failed(new OperationCanceledException(Stopped)));
    }
}

/// <summary>What the writer announces after a commit that wrote: the command that committed and the last sequence
/// it took from each mark it touched.</summary>
public sealed record Committed(object Command, IReadOnlyDictionary<string, long> Sequences);
