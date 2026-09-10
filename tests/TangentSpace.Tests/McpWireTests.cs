using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using TangentSpace.Mcp;
using Xunit;

namespace TangentSpace.Tests;

public sealed class McpWireTests
{
    private static (JObject Body, HeaderDictionary Headers) Request() => (JObject.Parse("""
        {"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"SelectCompanion","arguments":{},"_meta":{
          "io.modelcontextprotocol/protocolVersion":"2026-07-28",
          "io.modelcontextprotocol/clientInfo":{"name":"proof","version":"1"},
          "io.modelcontextprotocol/clientCapabilities":{}}}}
        """), new() { ["MCP-Protocol-Version"] = "2026-07-28", ["Mcp-Method"] = "tools/call", ["Mcp-Name"] = "SelectCompanion" });

    [Fact] public void CurrentRequestAndEncodedNameAreAccepted()
    {
        var (body, headers) = Request();
        Assert.True(McpWire.Validate(body, headers).Valid);
        headers["Mcp-Name"] = "=?base64?U2VsZWN0Q29tcGFuaW9u?=";
        Assert.True(McpWire.Validate(body, headers).Valid);
    }

    [Theory]
    [InlineData("Mcp-Method")]
    [InlineData("Mcp-Name")]
    [InlineData("MCP-Protocol-Version")]
    public void MissingAndMismatchedHeadersReject(string header)
    {
        var (body, headers) = Request();
        headers.Remove(header);
        Assert.Equal(-32020, McpWire.Validate(body, headers).ErrorCode);
        headers[header] = "wrong";
        Assert.Equal(-32020, McpWire.Validate(body, headers).ErrorCode);
    }

    [Fact] public void UnsupportedVersionListsSupportedVersions()
    {
        var (body, headers) = Request();
        body["params"]!["_meta"]!["io.modelcontextprotocol/protocolVersion"] = "1900-01-01";
        headers["MCP-Protocol-Version"] = "1900-01-01";
        var result = McpWire.Validate(body, headers);
        Assert.Equal(-32022, result.ErrorCode);
        Assert.NotEmpty((JArray)result.Data!["supported"]!);
    }

    [Fact] public void LegacyInitializeWorksWithoutModernMetadata()
    {
        var request = JObject.Parse("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25"}}""");
        Assert.True(McpWire.Validate(request, new HeaderDictionary()).Valid);
    }

    [Fact] public void MalformedMetadataAndRequestIdAreRejectedWithoutCastExceptions()
    {
        var (body, headers) = Request();
        body["params"]!["_meta"]!["io.modelcontextprotocol/clientInfo"] = "not-an-object";
        Assert.False(McpWire.Validate(body, headers).Valid);
        body["id"] = new JObject();
        Assert.False(McpWire.Validate(body, headers).Valid);
        body["jsonrpc"] = new JObject();
        Assert.False(McpWire.Validate(body, headers).Valid);
    }
}
