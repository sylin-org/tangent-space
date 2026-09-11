using System.Text;
using Newtonsoft.Json.Linq;

namespace TangentSpace.Mcp;

/// <summary>Validates the two supported MCP wire eras before dispatch; metadata is never identity.</summary>
public static class McpWire
{
    public sealed record Validation(string Version, int? ErrorCode = null, string? Error = null, JObject? Data = null)
    {
        public bool Valid => ErrorCode is null;
    }

    public static Validation Validate(JObject request, IHeaderDictionary headers)
    {
        if (String(request["jsonrpc"]) != "2.0" || String(request["method"]) is not { Length: > 0 and <= 128 } method
            || request["params"] is { } parameters && parameters is not JObject
            || request["id"] is { Type: not (JTokenType.String or JTokenType.Integer) }
            || String(request["id"]) is { Length: > 128 }
            || request["result"] is not null || request["error"] is not null)
            return Invalid("Expected a JSON-RPC request object.");
        if (request["id"] is null && method != "notifications/initialized")
            return Invalid("This endpoint requires a request ID.");

        var p = request["params"] as JObject;
        var meta = p?["_meta"] as JObject;
        var bodyVersion = String(meta?["io.modelcontextprotocol/protocolVersion"]);
        var headerVersion = Header(headers, "MCP-Protocol-Version");
        if (method == "initialize")
        {
            var initialized = String(p?["protocolVersion"]);
            return initialized == McpContractCatalog.CompatVersion
                ? new(McpContractCatalog.CompatVersion) : Unsupported(initialized ?? "");
        }
        if (method == "notifications/initialized" && bodyVersion is null)
            return new(McpContractCatalog.CompatVersion);
        if (headerVersion == McpContractCatalog.CompatVersion && bodyVersion is null)
            return new(McpContractCatalog.CompatVersion);
        if (bodyVersion is not null && headerVersion is not null && bodyVersion != headerVersion)
            return Mismatch();
        var version = bodyVersion ?? headerVersion;
        if (version is not null && version != McpContractCatalog.CurrentVersion) return Unsupported(version);
        if (headerVersion is null || bodyVersion is null || Header(headers, "Mcp-Method") != method) return Mismatch();
        if (meta?["io.modelcontextprotocol/clientCapabilities"] is not JObject
            || meta?["io.modelcontextprotocol/clientInfo"] is not JObject info
            || String(info["name"]) is not { Length: > 0 and <= 256 }
            || String(info["version"]) is not { Length: > 0 and <= 128 })
            return Invalid("Required client metadata is missing or malformed.");
        if (method is "tools/call" or "prompts/get" or "resources/read")
        {
            var name = String(p?[method == "resources/read" ? "uri" : "name"]);
            if (name is null || DecodeName(Header(headers, "Mcp-Name")) != name) return Mismatch();
        }
        return new(McpContractCatalog.CurrentVersion);
    }

    private static Validation Invalid(string message) => new("", -32600, message);
    private static Validation Mismatch() => new("", -32020, "Required MCP headers are missing, malformed, or disagree with the request body.");
    private static Validation Unsupported(string version) => new("", -32022, "Unsupported protocol version.",
        new JObject { ["supported"] = new JArray(McpContractCatalog.SupportedVersions), ["requested"] = version });
    private static string? String(JToken? value) => value?.Type == JTokenType.String ? value.Value<string>() : null;
    private static string? Header(IHeaderDictionary headers, string name)
        => headers.TryGetValue(name, out var values) && values.Count == 1 && values[0] is { Length: > 0 } value
            && value.All(c => c is >= ' ' and <= '~') ? value : null;

    private static string? DecodeName(string? value)
    {
        if (value is null || !value.StartsWith("=?base64?", StringComparison.Ordinal) || !value.EndsWith("?=", StringComparison.Ordinal)) return value;
        try { return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(value[9..^2])); }
        catch (Exception e) when (e is FormatException or DecoderFallbackException) { return null; }
    }
}
