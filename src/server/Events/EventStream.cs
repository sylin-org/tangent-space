namespace Tangent.Events;

/// <summary>The open streams of the event stream, in this process. <see cref="Open"/> admits a stream for a reader
/// within <see cref="PerReader"/> streams of theirs and <see cref="InAll"/> streams in all, or refuses it.
/// <see cref="Head"/> is the log's last committed sequence: read once from the log, then raised by each row of it once
/// its command commits (<see cref="Committed"/>), a compare-and-swap maximum, so the order commits are told in does not
/// matter: the log's committed numbers have no gap, so every row through the head is committed. A stream that begins at
/// it begins after every row already committed, and <see cref="Moved"/> wakes a stream when the head passes where it
/// read to; the stream reads the rows from the log itself.</summary>
public sealed class EventStream
{
    /// <summary>The most streams one reader holds open at once: a participant by their Id, a visitor by their address.</summary>
    public const int PerReader = 6;

    /// <summary>The most streams open at once in all.</summary>
    public const int InAll = 512;

    private readonly Dictionary<string, int> _perReader = new(StringComparer.Ordinal);
    private readonly Lock _counts = new();
    private int _inAll;
    private long _head = -1;
    private TaskCompletionSource _pulse = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The log's last committed sequence, read from the log the first time it is asked.</summary>
    public async ValueTask<long> Head(CancellationToken ct)
    {
        var head = Volatile.Read(ref _head);
        if (head >= 0) return head;
        Raise(await Event.Log.Highest(ct));
        return Volatile.Read(ref _head);
    }

    /// <summary>The row at <paramref name="seq"/> of the log has committed: raises the head to it and wakes every
    /// stream waiting on <see cref="Moved"/>.</summary>
    public void Committed(long seq)
    {
        Raise(seq);
        Interlocked.Exchange(ref _pulse, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }

    /// <summary>Whether the head is past <paramref name="cursor"/>, waiting at most <paramref name="wait"/> for a commit
    /// that moves it there.</summary>
    public async Task<bool> Moved(long cursor, TimeSpan wait, CancellationToken ct)
    {
        // The pulse is taken before the head is read: a commit after this read completes the pulse taken.
        var pulse = Volatile.Read(ref _pulse).Task;
        if (await Head(ct) > cursor) return true;
        try
        {
            await pulse.WaitAsync(wait, ct);
        }
        catch (TimeoutException)
        {
            return false;
        }

        return Volatile.Read(ref _head) > cursor;
    }

    /// <summary>A place for one more stream of <paramref name="reader"/>, or null when they hold <see cref="PerReader"/>
    /// streams or <see cref="InAll"/> are open. Disposing it gives the place back.</summary>
    public IDisposable? Open(string reader)
    {
        lock (_counts)
        {
            var held = _perReader.GetValueOrDefault(reader);
            if (held >= PerReader || _inAll >= InAll) return null;
            _perReader[reader] = held + 1;
            _inAll++;
        }

        return new Place(this, reader);
    }

    /// <summary>How many streams are open, in all and for one reader.</summary>
    public (int InAll, int Reader) Count(string reader)
    {
        lock (_counts) return (_inAll, _perReader.GetValueOrDefault(reader));
    }

    private void Raise(long seq)
    {
        var head = Volatile.Read(ref _head);
        while (seq > head)
        {
            var seen = Interlocked.CompareExchange(ref _head, seq, head);
            if (seen == head) return;
            head = seen;
        }
    }

    private void Close(string reader)
    {
        lock (_counts)
        {
            _inAll--;
            if (--_perReader[reader] == 0) _perReader.Remove(reader);
        }
    }

    /// <summary>One open stream's place among them, given back once.</summary>
    private sealed class Place(EventStream stream, string reader) : IDisposable
    {
        private int _closed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0) stream.Close(reader);
        }
    }
}
