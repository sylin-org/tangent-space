using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Tangent.Events;

/// <summary>The open streams of the event stream, in this process. <see cref="Open"/> admits a stream for a reader
/// within <see cref="PerReader"/> streams of theirs and <see cref="InAll"/> streams in all, or refuses it.
/// <see cref="Publish"/> queues an appended row of the log for every open stream at once and never waits: a stream
/// whose queue already holds <see cref="QueueBound"/> rows is a slow reader, and it is closed — its queue completed and
/// its request aborted — to resume from the last sequence it received. Who may see a row is each stream's to decide.
/// <see cref="Head"/> is the log's last sequence as this process knows it: loaded once from the log, then raised by
/// every row published, never ahead of what is committed.</summary>
public sealed class EventStream
{
    /// <summary>The most rows a stream's queue holds before the stream is closed as a slow reader.</summary>
    public const int QueueBound = 256;

    /// <summary>The most streams one reader holds open at once: a participant by their Id, a visitor by their address.</summary>
    public const int PerReader = 6;

    /// <summary>The most streams open at once in all.</summary>
    public const int InAll = 512;

    private readonly ConcurrentDictionary<Connection, byte> _open = new();
    private readonly Dictionary<string, int> _perReader = new(StringComparer.Ordinal);
    private readonly Lock _counts = new();
    private int _inAll;
    private long _head = -1;

    /// <summary>The log's last sequence as this process knows it, read from the log the first time it is asked.</summary>
    public async ValueTask<long> Head(CancellationToken ct)
    {
        var head = Volatile.Read(ref _head);
        if (head >= 0) return head;
        Raise(await Event.HighWaterMark.Load(ct));
        return Volatile.Read(ref _head);
    }

    /// <summary>A new stream for <paramref name="reader"/>, or null when they hold <see cref="PerReader"/> streams or
    /// <see cref="InAll"/> are open. <paramref name="abort"/> ends the stream's request when it is closed as a slow
    /// reader. Disposing the stream gives its place back.</summary>
    public Connection? Open(string reader, Action abort)
    {
        lock (_counts)
        {
            var held = _perReader.GetValueOrDefault(reader);
            if (held >= PerReader || _inAll >= InAll) return null;
            _perReader[reader] = held + 1;
            _inAll++;
        }

        var connection = new Connection(this, reader, abort);
        _open[connection] = 0;
        return connection;
    }

    /// <summary>Queues <paramref name="row"/> for every open stream, closing each whose queue is full.</summary>
    public void Publish(Event row)
    {
        Raise(row.Seq);
        foreach (var connection in _open.Keys) connection.Offer(row);
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

    private void Close(Connection connection)
    {
        if (!_open.TryRemove(connection, out _)) return;
        lock (_counts)
        {
            _inAll--;
            if (--_perReader[connection.Reader] == 0) _perReader.Remove(connection.Reader);
        }
    }

    /// <summary>One open stream's queue of appended rows, in the order they were published.</summary>
    public sealed class Connection : IDisposable
    {
        private readonly EventStream _stream;
        private readonly Action _abort;
        private readonly Channel<Event> _queue = Channel.CreateBounded<Event>(
            new BoundedChannelOptions(QueueBound) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        private int _slow;
        private int _closed;

        internal Connection(EventStream stream, string reader, Action abort)
        {
            _stream = stream;
            Reader = reader;
            _abort = abort;
        }

        public string Reader { get; }

        /// <summary>Whether the stream was closed because its queue was full.</summary>
        public bool Slow => Volatile.Read(ref _slow) == 1;

        public ChannelReader<Event> Queue => _queue.Reader;

        internal void Offer(Event row)
        {
            if (Volatile.Read(ref _closed) == 1 || _queue.Writer.TryWrite(row)) return;
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;
            Volatile.Write(ref _slow, 1);
            _queue.Writer.TryComplete();
            _stream.Close(this);
            try
            {
                _abort();
            }
            catch (ObjectDisposedException)
            {
                // The request has already ended.
            }
        }

        public void Dispose()
        {
            Volatile.Write(ref _closed, 1);
            _queue.Writer.TryComplete();
            _stream.Close(this);
        }
    }
}
