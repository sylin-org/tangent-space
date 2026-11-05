using Koan.Mcp.Hosting;
using Koan.Mcp.Options;
using Koan.Web.Hosting;
using Microsoft.Extensions.Options;

namespace Tangent.Space;

/// <summary>Where a standard MCP client takes part: Koan's edge at <see cref="Path"/>, a fixed path under the base URL,
/// whose every tool is a route of this place's, and <c>read</c>, which reads any of its addresses. A call carries the
/// caller's own session cookie through the same pipeline a page's request takes, and a caller with none is a visitor,
/// judged by each route's gate.</summary>
public static class ToolDoor
{
    /// <summary>The edge's path under the base URL, which is Koan's own.</summary>
    public const string Path = "/mcp";

    public static string Address(string baseUrl) => baseUrl + Path;

    /// <summary>What the edge tells a client in <c>initialize</c>: the one text's summary, tools and signing in
    /// (<see cref="LlmsText.Text.Instructions"/>), for the place as it is and at the address the client reached, read from
    /// the one row; nothing before first run, when there is no place to describe.</summary>
    public sealed class Instructions(KoanWebBaseUrl baseUrl, IHttpContextAccessor accessor, IOptions<McpServerOptions> edge) : IMcpInstructions
    {
        async Task<string?> IMcpInstructions.Instructions(CancellationToken ct)
        {
            if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return null;
            var address = accessor.HttpContext is { } http ? baseUrl.Resolve(http) : baseUrl.Current;
            return address is null ? null : LlmsText.For(space, address, baseUrl.Current is not null, edge.Value.ReadByAddress).Instructions;
        }
    }
}
