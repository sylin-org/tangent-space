using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Tangent.Tools;

/// <summary>A standard MCP client over Streamable HTTP, as a real host's <c>/mcp</c> takes one: it initializes, says it is
/// initialized, and then lists and calls, each request standing alone. Given a session, it sends that session's cookie with
/// every request, as a client signed in with its own account does; given none, it is a visitor. It follows no
/// redirect.</summary>
internal sealed class ToolClient : IDisposable
{
    public const string Path = "/mcp";

    private const string Version = "2025-06-18";

    private readonly HttpClient _http;
    private readonly bool _owned;
    private int _next;

    private ToolClient(HttpClient http, bool owned)
    {
        _http = http;
        _owned = owned;
    }

    /// <summary>What <c>initialize</c> answered.</summary>
    public JsonElement Initialized { get; private set; }

    /// <summary>A client connected with the cookie of <paramref name="session"/>, or as a visitor where there is none.</summary>
    public static async Task<ToolClient> Connect(TangentSpaceFixture tangentSpace, Participants.Browser? session = null)
    {
        var client = session is null
            ? new ToolClient(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = tangentSpace.Client.BaseAddress }, owned: true)
            : new ToolClient(session.Client, owned: false);
        client.Initialized = await client.Rpc("initialize", new
        {
            protocolVersion = Version,
            capabilities = new { },
            clientInfo = new { name = "tangent-tests", version = "1.0.0" }
        });
        using var said = Request(new { jsonrpc = "2.0", method = "notifications/initialized" });
        using var answered = await client._http.SendAsync(said, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, answered.StatusCode);
        return client;
    }

    /// <summary>The name of each tool listed, with what its input lists.</summary>
    public async Task<IReadOnlyDictionary<string, JsonElement>> Tools()
    {
        var listed = await Rpc("tools/list", new { });
        return listed.GetProperty("tools").EnumerateArray().ToDictionary(tool => tool.GetProperty("name").GetString()!, tool => tool.Clone());
    }

    /// <summary>One call of the tool <paramref name="name"/>, as its answer reads.</summary>
    public async Task<Called> Call(string name, object? arguments = null)
    {
        var result = await Rpc("tools/call", new { name, arguments = arguments ?? new { } });
        var meta = result.GetProperty("_meta");
        return new Called(
            meta.GetProperty("status").GetInt32(),
            result.TryGetProperty("isError", out var failed) && failed.GetBoolean(),
            result.GetProperty("content")[0].GetProperty("text").GetString() ?? "",
            result.TryGetProperty("structuredContent", out var structured) && structured.ValueKind == JsonValueKind.Object ? structured.Clone() : null,
            meta.TryGetProperty("location", out var location) ? location.GetString() : null,
            meta.TryGetProperty("errorCode", out var code) ? code.GetString() : null,
            meta.Clone());
    }

    public void Dispose()
    {
        if (_owned) _http.Dispose();
    }

    private async Task<JsonElement> Rpc(string method, object parameters)
    {
        using var asked = Request(new { jsonrpc = "2.0", id = ++_next, method, @params = parameters });
        using var answered = await _http.SendAsync(asked, TestContext.Current.CancellationToken);
        var text = await answered.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(answered.StatusCode == HttpStatusCode.OK, $"{method} answered {(int)answered.StatusCode}: {text}");
        var answer = JsonDocument.Parse(text).RootElement;
        Assert.False(answer.TryGetProperty("error", out var error), $"{method} failed: {error}");
        return answer.GetProperty("result").Clone();
    }

    /// <summary>A message to <c>/mcp</c>, as a program sends it: no browser's headers, and the cookie, where there is one,
    /// from the client's own jar; on the https the edge requires outside Development, as the reverse proxy of a loopback
    /// address says it.</summary>
    public static HttpRequestMessage Request(object message)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("MCP-Protocol-Version", Version);
        return request;
    }
}

/// <summary>A tool's answer: the status the route answered, whether the call failed, its text, what the route answered as
/// a JSON object, where it went, the kind of refusal, and the whole of its <c>_meta</c>.</summary>
internal sealed record Called(int Status, bool Failed, string Text, JsonElement? Structured, string? Location, string? ErrorCode, JsonElement Meta);
