using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Tangent.Events;

/// <summary>One open stream of the log, read as a browser reads server-sent events: frame by frame, each read bounded,
/// comments kept apart from the frames that carry an event.</summary>
internal sealed class Streamed : IAsyncDisposable
{
    /// <summary>The longest a proof waits for one frame.</summary>
    public static readonly TimeSpan Within = TimeSpan.FromSeconds(20);

    private readonly HttpResponseMessage _response;
    private readonly StreamReader _reader;

    private Streamed(HttpResponseMessage response, StreamReader reader)
    {
        _response = response;
        _reader = reader;
    }

    public HttpResponseMessage Response => _response;

    /// <summary>Opens the stream at <paramref name="path"/>, naming <paramref name="lastEventId"/> as a reconnecting
    /// stream does; answers the response whatever its status, with the body ready to read when it is a stream.</summary>
    public static async Task<Streamed> Open(HttpClient client, string path, string? lastEventId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(EventsController.StreamType));
        if (lastEventId is not null) request.Headers.Add(EventsController.LastEventId, lastEventId);
        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        return new Streamed(response, new StreamReader(body, Encoding.UTF8));
    }

    /// <summary>The next frame that carries an event, past any comment.</summary>
    public async Task<Frame> Next()
    {
        while (true)
        {
            var frame = await Any();
            if (frame.Data.Length > 0) return frame;
        }
    }

    /// <summary>The next frame, a comment included.</summary>
    public async Task<Frame> Any()
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bound.CancelAfter(Within);
        string? name = null, id = null, comment = null;
        var data = new StringBuilder();
        var any = false;
        while (true)
        {
            var line = await _reader.ReadLineAsync(bound.Token) ?? throw new EndOfStreamException("The stream ended.");
            if (line.Length == 0)
            {
                if (any) return new Frame(name, id, data.ToString(), comment);
                continue;
            }

            any = true;
            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? "" : line[(colon + 1)..].TrimStart(' ');
            switch (field)
            {
                case "": comment = value; break;
                case "event": name = value; break;
                case "id": id = value; break;
                case "data": data.Append(data.Length > 0 ? "\n" : "").Append(value); break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        _response.Dispose();
        await Task.CompletedTask;
    }

    /// <summary>One frame: its name, id, data and comment.</summary>
    public sealed record Frame(string? Event, string? Id, string Data, string? Comment)
    {
        public JsonElement Json => JsonDocument.Parse(Data).RootElement.Clone();

        public long Seq => long.Parse(Id!, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool Ok(Streamed streamed) => streamed.Response.StatusCode == HttpStatusCode.OK;
}
