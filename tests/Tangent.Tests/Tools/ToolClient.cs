using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Tangent.Tools;

/// <summary>A standard MCP client over Streamable HTTP, as a real host's <c>/mcp</c> takes one: it initializes with a bearer, says it is
/// initialized, and then lists and calls. It keeps no cookie and follows no redirect.</summary>
internal sealed class ToolClient : IDisposable
{
    public const string Path = "/mcp";

    private const string Version = "2025-06-18";

    private readonly HttpClient _http;
    private readonly string _bearer;
    private string? _session;
    private int _next;

    private ToolClient(TangentSpaceFixture tangentSpace, string bearer)
    {
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = tangentSpace.Client.BaseAddress };
        _bearer = bearer;
    }

    /// <summary>What <c>initialize</c> answered.</summary>
    public JsonElement Initialized { get; private set; }

    /// <summary>A client connected as whoever <paramref name="bearer"/> names.</summary>
    public static async Task<ToolClient> Connect(TangentSpaceFixture tangentSpace, string bearer)
    {
        var client = new ToolClient(tangentSpace, bearer);
        client.Initialized = await client.Rpc("initialize", new
        {
            protocolVersion = Version,
            capabilities = new { },
            clientInfo = new { name = "tangent-tests", version = "1.0.0" }
        });
        using var said = Request(new { jsonrpc = "2.0", method = "notifications/initialized" }, bearer, client._session);
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
            meta.TryGetProperty("errorCode", out var code) ? code.GetString() : null);
    }

    public void Dispose() => _http.Dispose();

    private async Task<JsonElement> Rpc(string method, object parameters)
    {
        using var asked = Request(new { jsonrpc = "2.0", id = ++_next, method, @params = parameters }, _bearer, _session);
        using var answered = await _http.SendAsync(asked, TestContext.Current.CancellationToken);
        var text = await answered.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(answered.StatusCode == HttpStatusCode.OK, $"{method} answered {(int)answered.StatusCode}: {text}");
        if (answered.Headers.TryGetValues("Mcp-Session-Id", out var session)) _session = session.Single();
        var data = text.ReplaceLineEndings("\n").Split('\n').First(line => line.StartsWith("data:", StringComparison.Ordinal))["data:".Length..];
        var answer = JsonDocument.Parse(data).RootElement;
        Assert.False(answer.TryGetProperty("error", out var error), $"{method} failed: {error}");
        return answer.GetProperty("result").Clone();
    }

    /// <summary>A message to <c>/mcp</c>, on the https the edge requires outside Development, as the reverse proxy of a
    /// loopback address says it.</summary>
    public static HttpRequestMessage Request(object message, string? bearer, string? session = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("MCP-Protocol-Version", Version);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (session is not null) request.Headers.Add("Mcp-Session-Id", session);
        return request;
    }
}

/// <summary>A tool's answer: the status the route answered, whether the call failed, its text, what the route answered as
/// a JSON object, where it went, and the kind of refusal.</summary>
internal sealed record Called(int Status, bool Failed, string Text, JsonElement? Structured, string? Location, string? ErrorCode);
