namespace Tangent.Space;

/// <summary>Where a standard MCP client takes part: Koan's edge at <see cref="Path"/>, a fixed path under the base URL,
/// whose every tool is a route of this place's. A call carries the caller's own session cookie through the same pipeline
/// a page's request takes, and a caller with none is a visitor, judged by each tool's gate.</summary>
public static class ToolDoor
{
    /// <summary>The edge's path under the base URL, which is Koan's own.</summary>
    public const string Path = "/mcp";

    public static string Address(string baseUrl) => baseUrl + Path;
}
