using System.Threading.Channels;
using Koan.Core.BackgroundServices;
using Koan.Data.Abstractions;
using Koan.Data.Core.Model;
using Koan.Data.Core.Sorting;
using Tangent.Writer;

namespace Tangent.Events;

/// <summary>Carries the writer's commits to the event stream through Koan Communication. A
/// <see cref="WriteLoop.Committed"/> handler notes the log's last sequence a commit took and returns at once; this
/// worker then reads the rows past the last one it announced, in sequence order and bounded pages, and raises
/// <see cref="Appended"/> on them, which Koan delivers to <see cref="StreamDelivery"/> in the same order. A page it could
/// not raise is logged and passed over: an open stream finds the rows it lacks in the log by their sequence, which has no
/// gap. Koan discovers it as a background service and starts and stops it with the host.</summary>
public sealed class LogAnnouncer(ILogger<LogAnnouncer> logger, IConfiguration configuration, WriteLoop writer)
    : KoanBackgroundServiceBase(logger, configuration)
{
    /// <summary>The most rows one read takes and one raise carries.</summary>
    public const int PageSize = 100;

    // Only the newest commit's sequence matters: a sequence covers every one before it.
    private readonly Channel<long> _committed = Channel.CreateBounded<long>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public override async Task ExecuteCore(CancellationToken ct)
    {
        // Rows already in the log when it starts are not announced; a commit that lands while it subscribes is, from the
        // log's mark read once the subscription holds.
        var announced = await Event.HighWaterMark.Load(ct);
        writer.Committed += Noted;
        try
        {
            announced = await Announce(announced, await Event.HighWaterMark.Load(ct), ct);
            while (await _committed.Reader.WaitToReadAsync(ct))
            {
                while (_committed.Reader.TryRead(out var through)) announced = await Announce(announced, through, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            writer.Committed -= Noted;
        }
    }

    private void Noted(Committed committed)
    {
        if (committed.Sequences.TryGetValue(Event.Log, out var through)) _committed.Writer.TryWrite(through);
    }

    /// <summary>Raises <see cref="Appended"/> on every row past <paramref name="announced"/> through
    /// <paramref name="through"/>, a page at a time, and answers the last sequence it reached.</summary>
    private async Task<long> Announce(long announced, long through, CancellationToken ct)
    {
        while (announced < through)
        {
            var from = announced;
            try
            {
                var rows = await Event.Query(e => e.Seq > from && e.Seq <= through,
                    new QueryDefinition { Page = 1, PageSize = PageSize }.WithSort<Event>(sort => sort.OrderBy(e => e.Seq)), ct);
                if (rows.Count == 0) return through;
                await rows.Events.Raise<Appended>(ct);
                announced = rows[^1].Seq;
            }
            catch (Exception failure) when (failure is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Logger.LogError(failure, "The log's rows past {Sequence} through {Through} could not be announced", from, through);
                return through;
            }
        }

        return announced;
    }
}
