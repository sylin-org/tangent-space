using Koan.Core;

namespace TangentSpace.Mcp;

/// <summary>Canonical public origin and transport bounds for the inbound MCP endpoint.</summary>
public sealed class McpOptions
{
    public const string Configuration = "Tangent:Mcp";
    public const int DefaultRequestLimitBytes = 64 * 1024;
    public const string TokenEndpoint = "/mcp/token";
    public const string WellKnownEndpoint = "/.well-known/tangent-mcp";

    /// <summary>Canonical absolute public origin, e.g. https://tangent.example. No path, query or userinfo.</summary>
    public string PublicBaseUrl { get; set; } = "";

    public int RequestLimitBytes { get; set; } = DefaultRequestLimitBytes;
}

public static class McpOptionsValidation
{
    public static bool IsCanonicalOrigin(string? value, out string origin)
    {
        origin = "";
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.Scheme == "http" && !uri.IsLoopback || uri.UserInfo.Length != 0
            || uri.AbsolutePath is not ("/" or "") || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return false;
        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
