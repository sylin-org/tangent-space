using TangentSpace.Hosting;
using TangentSpace.Participation;

namespace TangentSpace.Mcp;

// Also used by bearer-authenticated WebMCP requests. Header presence selects this
// handler even for an invalid token: failure must never fall back to a browser cookie.
public sealed class McpConsumerRegistration : IRegistration
{
    public string Name => "mcp";
    public string AuthenticationScheme => ParticipationConstants.Scheme;
    public bool Accepts(HttpRequest request) => request.Headers.ContainsKey("Authorization");
}
