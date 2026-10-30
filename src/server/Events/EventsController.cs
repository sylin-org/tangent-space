using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Koan.Data.Abstractions;
using Koan.Data.Core.Sorting;
using Koan.Web.Hosting;
using Koan.Web.Sse;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Tangent.Access;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Events;

/// <summary>The log for a reader, at one address both ways. With <c>Accept: text/event-stream</c> it is the stream: the
/// rows past <see cref="Since"/> — or past <c>Last-Event-ID</c> when a reconnecting stream sends it — that reach the
/// reader (<see cref="Readable"/>), oldest first, one row to a frame with its sequence as the frame's id and its kind
/// as the frame's name; first at most <see cref="ScanLimit"/> rows already in the log, read a bounded page at a time,
/// then each row as Koan Communication delivers it once its commit has landed, a row the delivery skipped read from the
/// log by its sequence. Where the log holds more than catching up reads, the stream ends there, to be resumed from its
/// last frame; a catch-up whose whole scan held no row the reader may see first sends a frame with no data whose id is
/// the scan's last sequence, so a stream never stalls behind hidden rows. A frame's id is otherwise always a row the
/// reader may see, so the position a reader resumes from says nothing of the Tangents they may not view but that a
/// whole scan of hidden rows passed. The stream sends a comment once <see cref="Heartbeat"/> has passed since it last
/// wrote, whatever arrived meanwhile, ends after <see cref="Lifetime"/>, ends when its reader is too slow for its
/// queue (<see cref="EventStream.QueueBound"/>) and when a refresh finds the place absent to them, and is refused
/// beyond <see cref="EventStream.PerReader"/> streams of one reader or <see cref="EventStream.InAll"/> in all. Without
/// that header it is the poll (<see cref="Poll"/>): one page of at most <see cref="PageSize"/> rows that reach the
/// reader, from at most <see cref="ScanLimit"/> rows read, with the address to ask again by the same rule. Without a
/// sequence both begin at the newest row the reader may see (<see cref="Readable.Newest"/>). A reader without
/// <see cref="Bit.View"/> on the place receives the door's gate. Nothing here writes, and no request waits on anything
/// but the store and its own stream's queue.</summary>
public sealed class EventsController(KoanWebBaseUrl baseUrl, EventStream stream, IOptions<MvcNewtonsoftJsonOptions> json) : ControllerBase
{
    public const string Path = "/events";

    /// <summary>The query parameter naming the sequence the answer begins after.</summary>
    public const string Since = "since";

    /// <summary>The header a reconnecting stream names its last frame's id in; it wins over <see cref="Since"/>.</summary>
    public const string LastEventId = "Last-Event-ID";

    public const string StreamType = "text/event-stream";

    /// <summary>The most rows one poll answers.</summary>
    public const int PageSize = 50;

    /// <summary>The most rows of the log one poll reads, and one stream reads catching up.</summary>
    public const int ScanLimit = Readable.ScanLimit;

    /// <summary>The most rows a stream reads from the log at once, catching up or filling what its delivery skipped.</summary>
    public const int ReadLimit = 100;

    /// <summary>The most rows a stream takes from its queue at once.</summary>
    public const int Batch = 32;

    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>How long a stream's reader waits before reconnecting, as the stream's first frame says.</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromSeconds(3);

    [HttpGet(Path)]
    public async Task<IActionResult> Get([FromQuery(Name = Since)] string? since, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        var personId = User.Identity?.IsAuthenticated == true ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
        var place = await Capabilities.Load(personId, null, null, ct);
        var streamed = Streamed(Request);
        if (!place.Exists) return Refused(DoorController.Gated(place.Gate(Bit.View) ?? Capabilities.Absent), streamed);

        var named = Request.Headers[LastEventId].FirstOrDefault() is { Length: > 0 } last ? last : since;
        long from;
        if (named is null) from = await Readable.Newest(personId, await stream.Head(ct), ct);
        else if (!long.TryParse(named, NumberStyles.None, CultureInfo.InvariantCulture, out from))
            return Refused(new Problem(StatusCodes.Status400BadRequest, "Not a sequence of the log",
                $"{Since} and {LastEventId} carry a sequence of the log, a whole number.").ToResult(), streamed);

        if (!streamed) return Ok(await Page(personId, from, ct));

        var http = HttpContext;
        var reader = personId ?? "visitor " + http.Connection.RemoteIpAddress;
        if (stream.Open(reader, http.Abort) is not { } connection)
        {
            Response.Headers.RetryAfter = ((int)Heartbeat.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            return Refused(new Problem(StatusCodes.Status429TooManyRequests, "Too many streams",
                $"A reader holds at most {EventStream.PerReader} streams at once, and the place {EventStream.InAll} in all; poll {Path} instead, or close one.").ToResult(), streamed);
        }

        // Given back when the response ends, whether or not the stream was ever read.
        Response.RegisterForDispose(connection);
        return Sse.Stream(Frames(connection, personId, from));
    }

    /// <summary>The poll's page past <paramref name="from"/>.</summary>
    private async Task<Poll> Page(string? personId, long from, CancellationToken ct)
    {
        var rows = await After(from, long.MaxValue, ScanLimit, ct);
        var readable = new Readable(personId);
        await readable.Prepare(rows, ct);
        var sent = rows.Where(readable.Reaches).Take(PageSize).Select(Poll.Row.Of).ToList();

        // The position moves to the newest row sent; past hidden rows only when a whole scan held none to send.
        var full = rows.Count == ScanLimit;
        var through = sent.Count > 0 ? sent[^1].Seq : full ? rows[^1].Seq : from;
        return new Poll(from, through, full || sent.Count == PageSize, sent, Next(through));
    }

    /// <summary>The stream's frames: an opening comment with the reconnect delay, the rows past <paramref name="from"/>
    /// already in the log, then each row its queue receives, until it ends.</summary>
    private async IAsyncEnumerable<SseEnvelope> Frames(EventStream.Connection connection, string? personId, long from,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using (connection)
        {
            var readable = new Readable(personId);
            var until = DateTimeOffset.UtcNow + Lifetime;
            var cursor = from;
            yield return new SseEnvelope(null, "", Retry: Retry, Comment: "open");
            var wrote = DateTimeOffset.UtcNow;

            // Catching up: at most a scan's worth of the log, a page at a time, keeping the heartbeat and the lifetime.
            var scanned = 0;
            var sent = 0;
            while (true)
            {
                if (!readable.Place || DateTimeOffset.UtcNow >= until) yield break;
                if (DateTimeOffset.UtcNow - wrote >= Heartbeat)
                {
                    yield return Beat;
                    wrote = DateTimeOffset.UtcNow;
                }

                var rows = await After(cursor, long.MaxValue, Math.Min(ReadLimit, ScanLimit - scanned), ct);
                var frames = await Framed(rows, readable, ct);
                foreach (var frame in frames) yield return frame;
                if (frames.Count > 0) wrote = DateTimeOffset.UtcNow;
                sent += frames.Count;
                scanned += rows.Count;
                if (rows.Count > 0) cursor = rows[^1].Seq;
                if (scanned < ScanLimit && rows.Count == ReadLimit) continue;
                if (scanned < ScanLimit) break;

                // The log holds more than one stream catches up on: it ends here, to be resumed from its last frame, or
                // past the whole scan where the scan held nothing to send.
                if (sent == 0) yield return new SseEnvelope(null, "", cursor.ToString(CultureInfo.InvariantCulture));
                yield break;
            }

            var batch = new List<Event>(Batch);
            while (readable.Place && until - DateTimeOffset.UtcNow is var left && left > TimeSpan.Zero)
            {
                var beat = Heartbeat - (DateTimeOffset.UtcNow - wrote);
                if (beat <= TimeSpan.Zero)
                {
                    yield return Beat;
                    wrote = DateTimeOffset.UtcNow;
                    continue;
                }

                // A wait that passes comes round again: the heartbeat goes out if it is due, or the lifetime ends the stream.
                var ready = await Ready(connection, Min(beat, left), ct);
                if (ready is null) continue;

                // Its queue completed: the stream was too slow, or is ending.
                if (ready == false) yield break;
                batch.Clear();
                while (batch.Count < Batch && connection.Queue.TryRead(out var row))
                {
                    if (row.Seq > cursor) batch.Add(row);
                }

                if (batch.Count == 0) continue;
                var rows = await Contiguous(cursor, batch, ct);
                var frames = await Framed(rows, readable, ct);
                foreach (var frame in frames) yield return frame;
                if (frames.Count > 0) wrote = DateTimeOffset.UtcNow;
                cursor = rows[^1].Seq;
            }
        }
    }

    /// <summary>The comment a stream sends when it has written nothing for <see cref="Heartbeat"/>.</summary>
    private static readonly SseEnvelope Beat = new(null, "", Comment: "heartbeat");

    /// <summary>The frames for the rows that reach the reader.</summary>
    private async Task<List<SseEnvelope>> Framed(IReadOnlyList<Event> rows, Readable readable, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        await readable.Prepare(rows, ct);
        var settings = json.Value.SerializerSettings;
        return rows.Where(readable.Reaches)
            .Select(row => new SseEnvelope(row.Kind, JsonConvert.SerializeObject(Poll.Row.Of(row), settings),
                row.Seq.ToString(CultureInfo.InvariantCulture)))
            .ToList();
    }

    /// <summary>The rows from the one after <paramref name="cursor"/> through the batch's last, in order: the batch, with
    /// any row its delivery skipped read from the log, which has no gap in its sequence.</summary>
    private static async Task<IReadOnlyList<Event>> Contiguous(long cursor, List<Event> batch, CancellationToken ct)
    {
        var rows = new List<Event>(batch.Count);
        var expected = cursor + 1;
        foreach (var row in batch)
        {
            while (row.Seq > expected)
            {
                var skipped = await After(expected - 1, row.Seq - 1, ReadLimit, ct);
                if (skipped.Count == 0) break;
                rows.AddRange(skipped);
                expected = skipped[^1].Seq + 1;
            }

            rows.Add(row);
            expected = row.Seq + 1;
        }

        return rows;
    }

    /// <summary>True when the queue holds a row, false when it has completed, null when <paramref name="wait"/> passed
    /// first.</summary>
    private static async Task<bool?> Ready(EventStream.Connection connection, TimeSpan wait, CancellationToken ct)
    {
        using var beat = CancellationTokenSource.CreateLinkedTokenSource(ct);
        beat.CancelAfter(wait);
        try
        {
            return await connection.Queue.WaitToReadAsync(beat.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>The rows past <paramref name="after"/> through <paramref name="through"/>, oldest first, at most
    /// <paramref name="limit"/>: one range of the log's sequence index.</summary>
    private static Task<IReadOnlyList<Event>> After(long after, long through, int limit, CancellationToken ct)
        => Event.Query(e => e.Seq > after && e.Seq <= through,
            new QueryDefinition { Page = 1, PageSize = limit }.WithSort<Event>(sort => sort.OrderBy(e => e.Seq)), ct);

    /// <summary>A refusal as a stream's reader reads it: problem JSON, since a stream has no page to show it in.</summary>
    private static IActionResult Refused(IActionResult refusal, bool streamed)
    {
        if (streamed && refusal is ObjectResult problem)
        {
            problem.ContentTypes.Clear();
            problem.ContentTypes.Add("application/problem+json");
        }

        return refusal;
    }

    private static bool Streamed(HttpRequest request)
        => request.GetTypedHeaders().Accept.Any(type => type.MediaType.Equals(StreamType, StringComparison.OrdinalIgnoreCase));

    private string Next(long through) => baseUrl.Resolve(HttpContext) + Path + "?" + Since + "=" + through.ToString(CultureInfo.InvariantCulture);

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;
}
